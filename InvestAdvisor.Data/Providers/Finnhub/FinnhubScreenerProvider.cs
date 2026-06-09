using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// Reads screener inputs from Finnhub's free-tier endpoints:
/// <c>/stock/metric</c> (fundamentals), <c>/stock/recommendation</c> (analyst trend),
/// <c>/stock/insider-transactions</c> (SEC Form 4). Each call goes through the shared rate
/// limiter and degrades to null / empty on failure or premium-gated data.
/// </summary>
public sealed class FinnhubScreenerProvider(
    HttpClient http,
    IRateLimiter rateLimiter,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubScreenerProvider>? logger = null) : IScreenerDataProvider
{
    private readonly FinnhubOptions _opts = options.Value;

    private string Key()
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Finnhub API key not configured. Set Finnhub:ApiKey via user-secrets or the FINNHUB_API_KEY env var.");
        return Uri.EscapeDataString(_opts.ApiKey);
    }

    public async Task<FundamentalsResult?> GetFundamentalsAsync(string ticker, CancellationToken ct = default)
    {
        await rateLimiter.WaitAsync(ct);
        var url = $"/api/v1/stock/metric?symbol={Uri.EscapeDataString(ticker)}&metric=all&token={Key()}";
        string json;
        try { json = await http.GetStringAsync(url, ct); }
        catch (Exception ex) { logger?.LogWarning(ex, "Finnhub /stock/metric failed for {Ticker}.", ticker); return null; }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("metric", out var m) || m.ValueKind != JsonValueKind.Object)
                return null;
            return new FundamentalsResult(
                MarketCap: Num(m, "marketCapitalization"),
                PeRatio: Num(m, "peTTM", "peBasicExclExtraTTM", "peNormalizedAnnual"),
                RevenueGrowthPct: Num(m, "revenueGrowthTTMYoy", "revenueGrowthQuarterlyYoy", "revenueGrowth3Y"),
                EpsGrowthPct: Num(m, "epsGrowthTTMYoy", "epsGrowthQuarterlyYoy", "epsGrowth3Y"),
                DebtToEquity: Num(m, "totalDebt/totalEquityQuarterly", "totalDebt/totalEquityAnnual", "longTermDebt/equityQuarterly"),
                PriceToFreeCashFlow: Num(m, "pfcfShareTTM", "pfcfShareAnnual", "currentEv/freeCashFlowTTM"),
                MomentumShort: Num(m, "13WeekPriceReturnDaily"),
                MomentumLong: Num(m, "26WeekPriceReturnDaily"),
                Beta: Num(m, "beta"),
                RawJson: m.GetRawText());
        }
        catch (Exception ex) { logger?.LogWarning(ex, "Finnhub /stock/metric parse failed for {Ticker}.", ticker); return null; }
    }

    public async Task<AnalystRatingResult?> GetLatestAnalystRatingAsync(string ticker, CancellationToken ct = default)
    {
        await rateLimiter.WaitAsync(ct);
        var url = $"/api/v1/stock/recommendation?symbol={Uri.EscapeDataString(ticker)}&token={Key()}";
        FinnhubRecommendation[]? rows;
        try { rows = await http.GetFromJsonAsync<FinnhubRecommendation[]>(url, ct); }
        catch (Exception ex) { logger?.LogWarning(ex, "Finnhub /stock/recommendation failed for {Ticker}.", ticker); return null; }

        var latest = rows?.Where(r => !string.IsNullOrWhiteSpace(r.Period))
                          .OrderByDescending(r => r.Period, StringComparer.Ordinal)
                          .FirstOrDefault();
        return latest is null
            ? null
            : new AnalystRatingResult(latest.Period, latest.StrongBuy, latest.Buy, latest.Hold, latest.Sell, latest.StrongSell);
    }

    public async Task<IReadOnlyList<InsiderTradeResult>> GetInsiderTradesAsync(string ticker, CancellationToken ct = default)
    {
        await rateLimiter.WaitAsync(ct);
        var url = $"/api/v1/stock/insider-transactions?symbol={Uri.EscapeDataString(ticker)}&token={Key()}";
        FinnhubInsiderResponse? resp;
        try { resp = await http.GetFromJsonAsync<FinnhubInsiderResponse>(url, ct); }
        catch (Exception ex) { logger?.LogWarning(ex, "Finnhub /stock/insider-transactions failed for {Ticker}.", ticker); return Array.Empty<InsiderTradeResult>(); }

        if (resp?.Data is null) return Array.Empty<InsiderTradeResult>();
        var list = new List<InsiderTradeResult>(resp.Data.Length);
        foreach (var d in resp.Data)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            var fd = DateTime.TryParse(d.FilingDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var p) ? p : default;
            list.Add(new InsiderTradeResult(d.Name!, d.Change, d.Share, fd, d.TransactionCode ?? string.Empty, d.IsDerivative));
        }
        return list;
    }

    private static decimal? Num(JsonElement metric, params string[] keys)
    {
        foreach (var k in keys)
            if (metric.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
                return d;
        return null;
    }

    private sealed record FinnhubRecommendation(
        [property: JsonPropertyName("period")] string Period,
        [property: JsonPropertyName("strongBuy")] int StrongBuy,
        [property: JsonPropertyName("buy")] int Buy,
        [property: JsonPropertyName("hold")] int Hold,
        [property: JsonPropertyName("sell")] int Sell,
        [property: JsonPropertyName("strongSell")] int StrongSell);

    private sealed record FinnhubInsiderResponse(
        [property: JsonPropertyName("data")] FinnhubInsiderRow[]? Data,
        [property: JsonPropertyName("symbol")] string? Symbol);

    private sealed record FinnhubInsiderRow(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("share")] decimal Share,
        [property: JsonPropertyName("change")] decimal Change,
        [property: JsonPropertyName("filingDate")] string? FilingDate,
        [property: JsonPropertyName("transactionCode")] string? TransactionCode,
        [property: JsonPropertyName("isDerivative")] bool IsDerivative);
}
