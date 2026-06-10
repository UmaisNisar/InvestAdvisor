using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Fans out across the registered <see cref="ISocialFeedProvider"/>s (StockTwits, Reddit, …) to pull
/// recent posts for the screener universe plus the user's tracked tickers, persisting new ones as
/// unscored <see cref="NewsItem"/> rows. The sentiment scorer then grades them on the same scale as
/// the curated news feed. Mirrors <see cref="NewsRefreshService"/>'s URL de-dup and cadence guard.
/// </summary>
public sealed class SocialRefreshService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IEnumerable<ISocialFeedProvider> providers,
    ISystemClock clock,
    ILogger<SocialRefreshService>? logger = null) : ISocialRefreshService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private const int MaxTickers = 40;       // cap per run to bound rate-limit/API spend
    private const int MaxTextLength = 1000;

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        var providerList = providers.ToList();
        if (providerList.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Cadence: skip if any social row (any channel other than curated News) was fetched recently.
        var lastFetch = await db.NewsItems.AsNoTracking()
            .Where(n => n.Channel != Core.Enums.NewsSource.News)
            .OrderByDescending(n => n.FetchedAtUtc)
            .Select(n => (DateTime?)n.FetchedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (lastFetch is not null && (clock.UtcNow - lastFetch.Value) < RefreshInterval)
            return 0;

        var tickers = await GatherTickersAsync(db, ct);
        if (tickers.Count == 0) return 0;

        var existing = new HashSet<string>(
            await db.NewsItems.AsNoTracking().Select(n => n.Url).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var written = 0;
        foreach (var provider in providerList)
        {
            foreach (var ticker in tickers)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<SocialPost> posts;
                try { posts = await provider.GetTickerPostsAsync(ticker, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Social refresh failed for {Channel}/{Ticker}; continuing.",
                        provider.Channel, ticker);
                    continue;
                }
                written += PersistNew(db, posts, existing);
            }
        }

        if (written > 0) await db.SaveChangesAsync(ct);
        logger?.LogInformation("Social refresh wrote {Written} posts across {Providers} provider(s), {Tickers} tickers.",
            written, providerList.Count, tickers.Count);
        return written;
    }

    private static async Task<List<string>> GatherTickersAsync(InvestAdvisorDbContext db, CancellationToken ct)
    {
        // Tracked names first (they matter most to the user), then the rest of the active universe.
        var tracked = await db.Holdings.AsNoTracking().Select(h => h.Ticker)
            .Union(db.WatchlistItems.AsNoTracking().Select(w => w.Ticker))
            .ToListAsync(ct);
        var universe = await db.Stocks.AsNoTracking().Where(s => s.IsActive).Select(s => s.Ticker).ToListAsync(ct);

        return tracked.Concat(universe)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTickers)
            .ToList();
    }

    private int PersistNew(InvestAdvisorDbContext db, IReadOnlyList<SocialPost> posts, HashSet<string> existingUrls)
    {
        var added = 0;
        var fetchedAt = clock.UtcNow;
        foreach (var p in posts)
        {
            if (string.IsNullOrEmpty(p.Url) || !existingUrls.Add(p.Url)) continue;
            db.NewsItems.Add(new NewsItem
            {
                Ticker = p.Ticker,
                Headline = Truncate(p.Text, MaxTextLength),
                Source = p.Source,
                Url = p.Url,
                Channel = p.Channel,
                PublishedAtUtc = p.CreatedAtUtc,
                FetchedAtUtc = fetchedAt,
                // SentimentScoredAtUtc left null so the scorer grades it on the shared scale.
            });
            added++;
        }
        return added;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
