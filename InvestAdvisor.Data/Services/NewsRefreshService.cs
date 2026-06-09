using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

public sealed class NewsRefreshService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    INewsProvider provider,
    ISystemClock clock,
    ILogger<NewsRefreshService>? logger = null) : INewsRefreshService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    public async Task<int> RefreshAsync(IEnumerable<TickerSpec> tickers, CancellationToken ct = default)
    {
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

        var perTickerCalls = tickers.Distinct().Take(20).ToArray(); // cap to avoid rate-limit burn
        foreach (var spec in perTickerCalls)
        {
            IReadOnlyList<NewsHeadline> headlines;
            try { headlines = await provider.GetTickerNewsAsync(spec.Ticker, ct); }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "News refresh failed for {Ticker}; continuing.", spec.Ticker);
                continue;
            }
            written += PersistNew(db, headlines, spec.Ticker, existing);
        }

        try
        {
            var market = await provider.GetMarketNewsAsync(ct);
            written += PersistNew(db, market, fallbackTicker: null, existing);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Market news refresh failed; continuing.");
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
