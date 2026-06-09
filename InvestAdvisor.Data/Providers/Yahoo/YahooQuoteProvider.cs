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

        var prev = meta.ChartPreviousClose ?? meta.PreviousClose ?? price;
        var pct = prev == 0m ? 0m : (price - prev) / prev * 100m;
        return new Quote(ticker.ToUpperInvariant(), assetClass, price, prev, pct, clock.UtcNow);
    }

    private sealed record YahooChartResponse([property: JsonPropertyName("chart")] YahooChart? Chart);
    private sealed record YahooChart([property: JsonPropertyName("result")] YahooResult[]? Result);
    private sealed record YahooResult([property: JsonPropertyName("meta")] YahooMeta? Meta);
    private sealed record YahooMeta(
        [property: JsonPropertyName("regularMarketPrice")] decimal? RegularMarketPrice,
        [property: JsonPropertyName("chartPreviousClose")] decimal? ChartPreviousClose,
        [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
        [property: JsonPropertyName("currency")] string? Currency);
}
