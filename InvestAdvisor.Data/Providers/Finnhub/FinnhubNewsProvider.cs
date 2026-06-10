using System.Net.Http.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// Reads news from Finnhub:
/// company-specific via <c>/company-news?symbol=...&amp;from=...&amp;to=...</c>;
/// market-wide via <c>/news?category=general</c>.
/// </summary>
public sealed class FinnhubNewsProvider(
    HttpClient http,
    IRateLimiter rateLimiter,
    ISystemClock clock,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubNewsProvider>? logger = null) : INewsProvider
{
    private const int LookbackHours = 48;
    private const int MaxItems = 50;

    private readonly FinnhubOptions _opts = options.Value;

    public async Task<IReadOnlyList<NewsHeadline>> GetTickerNewsAsync(string ticker, CancellationToken ct = default)
    {
        EnsureKey();
        await rateLimiter.WaitAsync(ct);

        var to = clock.UtcNow;
        var from = to.AddHours(-LookbackHours);
        var url = $"/api/v1/company-news?symbol={Uri.EscapeDataString(ticker)}" +
                  $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={Uri.EscapeDataString(_opts.ApiKey)}";

        FinnhubNewsItem[]? items;
        try
        {
            items = await http.GetFromJsonAsync<FinnhubNewsItem[]>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub /company-news failed for {Ticker}.", ticker);
            return Array.Empty<NewsHeadline>();
        }

        return Map(items, fallbackTicker: ticker);
    }

    public async Task<IReadOnlyList<NewsHeadline>> GetMarketNewsAsync(CancellationToken ct = default)
    {
        EnsureKey();
        await rateLimiter.WaitAsync(ct);

        var url = $"/api/v1/news?category=general&token={Uri.EscapeDataString(_opts.ApiKey)}";

        FinnhubNewsItem[]? items;
        try
        {
            items = await http.GetFromJsonAsync<FinnhubNewsItem[]>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Finnhub /news failed.");
            return Array.Empty<NewsHeadline>();
        }

        return Map(items, fallbackTicker: null);
    }

    private static IReadOnlyList<NewsHeadline> Map(FinnhubNewsItem[]? items, string? fallbackTicker)
    {
        if (items is null || items.Length == 0) return Array.Empty<NewsHeadline>();
        var cutoff = DateTime.UtcNow.AddHours(-LookbackHours);

        return items
            .Where(i => !string.IsNullOrEmpty(i.Headline) && !string.IsNullOrEmpty(i.Url))
            .Select(i => new NewsHeadline(
                // Finnhub sends Related as "" (not null) on market news; an empty-string ticker
                // would dodge the "market-wide" (null) classification downstream.
                Ticker: string.IsNullOrWhiteSpace(i.Related) ? fallbackTicker : i.Related,
                Headline: i.Headline!,
                Source: i.Source ?? "",
                Url: i.Url!,
                PublishedAtUtc: DateTimeOffset.FromUnixTimeSeconds(i.DateTimeUnix).UtcDateTime))
            .Where(h => h.PublishedAtUtc >= cutoff)
            .OrderByDescending(h => h.PublishedAtUtc)
            .Take(MaxItems)
            .ToArray();
    }

    private void EnsureKey()
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Finnhub API key not configured. Set Finnhub:ApiKey via user-secrets or the FINNHUB_API_KEY env var.");
    }
}
