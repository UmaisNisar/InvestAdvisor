using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers.Yahoo;

/// <summary>
/// Quotes from Yahoo Finance's public chart endpoint. Free, no key, and crucially it covers
/// non-US exchanges (e.g. <c>.TO</c> Toronto, <c>.AX</c> Sydney) that Finnhub's free tier won't.
/// Unofficial, so it degrades to null on any failure.
/// </summary>
public sealed class YahooQuoteProvider(
    HttpClient http,
    ISystemClock clock,
    ILogger<YahooQuoteProvider>? logger = null)
{
    // Exchange suffixes Yahoo keeps as ".XX"; anything else trailing a dot is a share class.
    private static readonly HashSet<string> Exchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "TO", "V", "NE", "CN", "AX", "L", "AS", "PA", "DE", "MI", "MC", "ST", "HE", "OL", "BR",
        "LS", "VI", "IR", "SW", "HK", "T", "SS", "SZ", "SI", "KS", "KQ", "TW", "NS", "BO", "SA", "MX", "JO", "TA", "NZ",
    };

    /// <summary>Yahoo uses a dash for share classes (BRK-B, IDIV-B.TO) but keeps the exchange
    /// suffix as ".XX". Users type the broker form (IDIV.B.TO), so translate.</summary>
    internal static string ToYahooSymbol(string ticker)
    {
        ticker = ticker.Trim();
        var lastDot = ticker.LastIndexOf('.');
        if (lastDot > 0 && Exchanges.Contains(ticker[(lastDot + 1)..]))
            return $"{ticker[..lastDot].Replace('.', '-')}.{ticker[(lastDot + 1)..]}";
        return ticker.Replace('.', '-');
    }

    public async Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct = default)
    {
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(ToYahooSymbol(ticker))}?interval=1d&range=5d";
        YahooChartResponse? body;
        try { body = await http.GetFromJsonAsync<YahooChartResponse>(url, ct); }
        catch (Exception ex) { logger?.LogWarning(ex, "Yahoo chart failed for {Ticker}.", ticker); return null; }

        var meta = body?.Chart?.Result?.FirstOrDefault()?.Meta;
        if (meta?.RegularMarketPrice is not { } price || price <= 0m)
        {
            logger?.LogInformation("Yahoo returned no price for {Ticker}.", ticker);
            return null;
        }

        // Use previousClose (the actual prior-session close) for the daily change. chartPreviousClose
        // is the close *before the requested range* — with range=5d that's ~a week ago, which would
        // turn this into a 5-day return rather than today's change.
        var prev = meta.PreviousClose ?? meta.ChartPreviousClose ?? price;
        var pct = prev == 0m ? 0m : (price - prev) / prev * 100m;
        return new Quote(ticker.ToUpperInvariant(), assetClass, price, prev, pct, clock.UtcNow);
    }

    /// <summary>
    /// OHLCV bars from the same chart endpoint as <see cref="GetQuoteAsync"/>; only the
    /// range/interval differ (intraday bars for 1D/1W, daily otherwise). Bars Yahoo emits as
    /// null (holidays, halts) are dropped.
    /// </summary>
    public async Task<PriceHistory?> GetHistoryAsync(string ticker, AssetClass assetClass, HistoryRange range, CancellationToken ct = default)
    {
        var (yRange, yInterval) = range switch
        {
            HistoryRange.OneDay => ("1d", "5m"),
            HistoryRange.OneWeek => ("5d", "60m"),
            HistoryRange.OneMonth => ("1mo", "1d"),
            HistoryRange.ThreeMonths => ("3mo", "1d"),
            HistoryRange.SixMonths => ("6mo", "1d"),
            _ => ("1y", "1d"),
        };
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(ToYahooSymbol(ticker))}?interval={yInterval}&range={yRange}";
        YahooChartResponse? body;
        try { body = await http.GetFromJsonAsync<YahooChartResponse>(url, ct); }
        catch (Exception ex) { logger?.LogWarning(ex, "Yahoo history failed for {Ticker}.", ticker); return null; }

        var result = body?.Chart?.Result?.FirstOrDefault();
        var timestamps = result?.Timestamp;
        var q = result?.Indicators?.Quote?.FirstOrDefault();
        if (result is null || timestamps is null || q is null || timestamps.Length == 0)
        {
            logger?.LogInformation("Yahoo returned no history for {Ticker}.", ticker);
            return null;
        }

        var candles = new List<Candle>(timestamps.Length);
        for (var i = 0; i < timestamps.Length; i++)
        {
            // Yahoo aligns each array to the timestamp index and emits null for gap sessions; an
            // incomplete bar (any OHLC missing) is useless for a candle, so skip it entirely.
            if (q.Open?[i] is not { } o || q.High?[i] is not { } h ||
                q.Low?[i] is not { } l || q.Close?[i] is not { } c)
                continue;
            var time = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime;
            candles.Add(new Candle(time, o, h, l, c, q.Volume?[i] ?? 0));
        }

        if (candles.Count == 0) return null;
        return new PriceHistory(ticker.ToUpperInvariant(), result.Meta?.Currency ?? "USD", candles);
    }

    private sealed record YahooChartResponse([property: JsonPropertyName("chart")] YahooChart? Chart);
    private sealed record YahooChart([property: JsonPropertyName("result")] YahooResult[]? Result);
    private sealed record YahooResult(
        [property: JsonPropertyName("meta")] YahooMeta? Meta,
        [property: JsonPropertyName("timestamp")] long[]? Timestamp,
        [property: JsonPropertyName("indicators")] YahooIndicators? Indicators);
    private sealed record YahooIndicators([property: JsonPropertyName("quote")] YahooQuoteArrays[]? Quote);
    private sealed record YahooQuoteArrays(
        [property: JsonPropertyName("open")] decimal?[]? Open,
        [property: JsonPropertyName("high")] decimal?[]? High,
        [property: JsonPropertyName("low")] decimal?[]? Low,
        [property: JsonPropertyName("close")] decimal?[]? Close,
        [property: JsonPropertyName("volume")] long?[]? Volume);
    private sealed record YahooMeta(
        [property: JsonPropertyName("regularMarketPrice")] decimal? RegularMarketPrice,
        [property: JsonPropertyName("chartPreviousClose")] decimal? ChartPreviousClose,
        [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
        [property: JsonPropertyName("currency")] string? Currency);
}
