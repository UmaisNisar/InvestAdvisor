using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Providers.Yahoo;

/// <summary>
/// Ticker news from Yahoo Finance's per-symbol RSS feed
/// (<c>feeds.finance.yahoo.com/rss/2.0/headline?s=…</c>). Free, no key, and crucially it covers
/// the non-US exchanges (<c>.TO</c>, <c>.V</c>, <c>.AX</c>, …) that Finnhub's free-tier
/// company-news returns nothing for. The JSON search endpoint was rejected on purpose: its
/// news array is trending content, not symbol-specific. Unofficial, so it degrades to empty on
/// any failure. Market-wide news stays with Finnhub, so <see cref="GetMarketNewsAsync"/> is
/// intentionally empty here.
/// </summary>
public sealed class YahooNewsProvider(
    HttpClient http,
    ISystemClock clock,
    ILogger<YahooNewsProvider>? logger = null) : INewsProvider
{
    private const int LookbackHours = 48;
    private const int MaxItems = 15;

    public async Task<IReadOnlyList<NewsHeadline>> GetTickerNewsAsync(string ticker, CancellationToken ct = default)
    {
        var symbol = YahooQuoteProvider.ToYahooSymbol(ticker);
        var url = $"/rss/2.0/headline?s={Uri.EscapeDataString(symbol)}&region=US&lang=en-US";

        string xml;
        try { xml = await http.GetStringAsync(url, ct); }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Yahoo news RSS failed for {Ticker}.", ticker);
            return Array.Empty<NewsHeadline>();
        }

        var cutoff = clock.UtcNow.AddHours(-LookbackHours);
        var tagged = ticker.Trim().ToUpperInvariant();
        try
        {
            return XDocument.Parse(xml).Descendants("item")
                .Select(item => new
                {
                    Title = item.Element("title")?.Value,
                    Link = item.Element("link")?.Value,
                    PubDate = item.Element("pubDate")?.Value,
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Link))
                .Select(i => new NewsHeadline(
                    // The requested (broker-form) ticker, so the row joins against the user's
                    // holdings/watchlist spelling.
                    Ticker: tagged,
                    Headline: i.Title!.Trim(),
                    Source: "Yahoo Finance",
                    Url: i.Link!.Trim(),
                    PublishedAtUtc: ParsePubDate(i.PubDate)))
                .Where(h => h.PublishedAtUtc >= cutoff)
                .OrderByDescending(h => h.PublishedAtUtc)
                .Take(MaxItems)
                .ToArray();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Yahoo news RSS parse failed for {Ticker}.", ticker);
            return Array.Empty<NewsHeadline>();
        }
    }

    public Task<IReadOnlyList<NewsHeadline>> GetMarketNewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NewsHeadline>>(Array.Empty<NewsHeadline>());

    // RSS pubDate is RFC-822 ("Wed, 10 Jun 2026 22:28:51 +0000"), which TryParse accepts. An
    // unparseable date maps to MinValue, which the lookback cutoff then drops — better than
    // guessing "now" and presenting an undatable headline as fresh.
    private static DateTime ParsePubDate(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? dto.UtcDateTime
            : DateTime.MinValue;
}
