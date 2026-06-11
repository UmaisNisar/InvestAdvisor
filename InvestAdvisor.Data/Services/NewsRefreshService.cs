using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Refreshes ticker + market news into <c>NewsItems</c>. Providers are tried in registration
/// order per ticker and the first one with coverage wins — Finnhub answers US names, Yahoo
/// picks up the non-US exchanges Finnhub's free tier returns nothing for.
/// </summary>
public sealed class NewsRefreshService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IEnumerable<INewsProvider> providers,
    ISystemClock clock,
    ILogger<NewsRefreshService>? logger = null) : INewsRefreshService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private const int MaxTickersPerRun = 20; // cap to avoid rate-limit burn

    public async Task<int> RefreshAsync(IEnumerable<TickerSpec> tickers, CancellationToken ct = default)
    {
        var providerList = providers.ToList();
        if (providerList.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var lastFetch = await db.NewsItems.AsNoTracking()
            .OrderByDescending(n => n.FetchedAtUtc)
            .Select(n => (DateTime?)n.FetchedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (lastFetch is not null && (clock.UtcNow - lastFetch.Value) < RefreshInterval)
            return 0;

        var written = 0;
        var existing = new HashSet<string>(
            await db.NewsItems.AsNoTracking().Select(n => n.Url).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        // Rotate fairly: least-recently-fetched tickers first, so a list longer than the
        // per-run cap still gets every name covered across successive runs (a fixed-order
        // Take would starve everything past the first 20 forever).
        var lastFetchByTicker = (await db.NewsItems.AsNoTracking()
                .Where(n => n.Ticker != null)
                .GroupBy(n => n.Ticker!)
                .Select(g => new { Ticker = g.Key, Last = g.Max(n => n.FetchedAtUtc) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Ticker, x => x.Last, StringComparer.OrdinalIgnoreCase);

        var perTickerCalls = tickers.Distinct()
            .OrderBy(t => lastFetchByTicker.TryGetValue(t.Ticker, out var last) ? last : DateTime.MinValue)
            .ThenBy(t => t.Ticker, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTickersPerRun)
            .ToArray();
        foreach (var spec in perTickerCalls)
        {
            // First provider with coverage wins — no duplicate API spend on tickers the
            // primary already answers, while gaps fall through to the next provider.
            foreach (var provider in providerList)
            {
                IReadOnlyList<NewsHeadline> headlines;
                try { headlines = await provider.GetTickerNewsAsync(spec.Ticker, ct); }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "News refresh failed for {Ticker} via {Provider}; trying next.",
                        spec.Ticker, provider.GetType().Name);
                    continue;
                }
                if (headlines.Count == 0) continue;
                written += PersistNew(db, headlines, spec.Ticker, existing);
                break;
            }
        }

        foreach (var provider in providerList)
        {
            try
            {
                var market = await provider.GetMarketNewsAsync(ct);
                if (market.Count == 0) continue;
                written += PersistNew(db, market, fallbackTicker: null, existing);
                break;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Market news refresh failed via {Provider}; trying next.",
                    provider.GetType().Name);
            }
        }

        if (written > 0) await db.SaveChangesAsync(ct);
        return written;
    }

    private int PersistNew(
        InvestAdvisorDbContext db,
        IReadOnlyList<NewsHeadline> headlines,
        string? fallbackTicker,
        HashSet<string> existingUrls)
    {
        var added = 0;
        var fetchedAt = clock.UtcNow;
        foreach (var h in headlines)
        {
            if (string.IsNullOrEmpty(h.Url) || !existingUrls.Add(h.Url)) continue;
            db.NewsItems.Add(new NewsItem
            {
                Ticker = h.Ticker ?? fallbackTicker,
                Headline = h.Headline,
                Source = h.Source,
                Url = h.Url,
                PublishedAtUtc = h.PublishedAtUtc,
                FetchedAtUtc = fetchedAt,
            });
            added++;
        }
        return added;
    }
}
