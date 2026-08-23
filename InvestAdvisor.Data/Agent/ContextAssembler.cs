using InvestAdvisor.Core.Text;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Agent;

/// <summary>
/// Builds the <see cref="RunContext"/> payload sent to the LLM. All arithmetic is here —
/// the model never does math on raw prices.
/// </summary>
public sealed class ContextAssembler(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock,
    IFxRateProvider fx,
    ISentimentScoringService sentiment,
    ILogger<ContextAssembler>? logger = null) : IContextAssembler
{
    private const int MaxNewsItems = 25;
    private const int MaxHeadlineLength = 200;
    private const int TopMoversCount = 5;
    private static readonly TimeSpan NewsWindow = TimeSpan.FromHours(24);

    public async Task<RunContext> BuildAsync(int tenantId, RunTrigger trigger, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var settings = await settingsStore.GetAsync(ct);
        var freshnessCutoff = now - TimeSpan.FromSeconds(settings.MinPriceFreshnessSeconds);
        var newsCutoff = now - NewsWindow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.TenantId == tenantId, ct);
        var holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tenantId).OrderBy(h => h.Ticker).ToListAsync(ct);
        var watchlist = await db.WatchlistItems.AsNoTracking().Where(w => w.TenantId == tenantId).OrderBy(w => w.Ticker).ToListAsync(ct);

        var trackedTickers = holdings.Select(h => h.Ticker)
            .Concat(watchlist.Select(w => w.Ticker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Condition triggers review ONE event (lean prompt): scope news/sentiment to the
        // affected ticker so input tokens aren't spent on names the model won't re-rate.
        var focusTicker = trigger.Kind is RunTriggerKind.PriceTarget
            or RunTriggerKind.BigMove or RunTriggerKind.DriftThreshold
            ? trigger.Ticker : null;
        var newsScope = focusTicker is not null ? new[] { focusTicker } : trackedTickers;

        var latestSnapshots = await LoadLatestSnapshotsAsync(db, trackedTickers, ct);

        // Only this user's tracked names plus market-wide items. The same table also holds
        // screener-universe social posts, which would otherwise crowd out the holdings' actual
        // news within the item cap. Market-wide means null ticker — or "" on rows written
        // before FinnhubNewsProvider normalized Finnhub's empty Related field.
        var news = await db.NewsItems.AsNoTracking()
            .Where(n => n.FetchedAtUtc >= newsCutoff
                        && (n.Ticker == null || n.Ticker == "" || newsScope.Contains(n.Ticker)))
            .OrderByDescending(n => n.PublishedAtUtc)
            .Take(MaxNewsItems)
            .ToListAsync(ct);

        // Latest screener metrics give the model 13-week/26-week momentum (7d/30d for crypto)
        // per holding — far better trend evidence than a single day's move.
        var metricsByTicker = (await db.StockMetrics.AsNoTracking()
                .Where(m => trackedTickers.Contains(m.Ticker))
                .ToListAsync(ct))
            .GroupBy(m => m.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.FetchedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var profileSnapshot = new ProfileSnapshot(
            GoalsText: profile.GoalsText,
            RiskTolerance: profile.RiskTolerance.ToString(),
            TimeHorizon: profile.TimeHorizon.ToString(),
            DriftPctThreshold: profile.DriftPctThreshold,
            SingleDayMovePctThreshold: profile.SingleDayMovePctThreshold,
            RebalanceCadenceHours: profile.RebalanceCadenceHours);

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        foreach (var c in holdings.Select(h => Currency.Normalize(h.Currency)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);

        var (holdingViews, _) = PortfolioCalculator.HoldingViews(holdings, latestSnapshots, rates, freshnessCutoff, metricsByTicker);
        var totals = PortfolioCalculator.Totals(holdings, latestSnapshots, rates);
        var allocation = PortfolioCalculator.Allocation(holdings, holdingViews, totals.MarketValueUsd);
        var movers = PortfolioCalculator.TopMovers(latestSnapshots.Values, TopMoversCount, freshnessCutoff);

        var newsHeadlines = news.Select(n => new NewsHeadline(
            Ticker: n.Ticker,
            Headline: Strings.Truncate(n.Headline, MaxHeadlineLength),
            Source: n.Source,
            Url: n.Url,
            PublishedAtUtc: n.PublishedAtUtc,
            SentimentScore: n.SentimentScore,
            SentimentLabel: n.SentimentLabel)).ToArray();

        // Per-ticker sentiment digest, scoped to the names the user actually tracks
        // (or just the triggering ticker on a condition run).
        var sentimentByTicker = await sentiment.GetTickerSentimentAsync(ct);
        var sentimentViews = newsScope
            .Where(t => sentimentByTicker.ContainsKey(t))
            .Select(t =>
            {
                var s = sentimentByTicker[t];
                return new TickerSentimentView(t, s.MeanScore, s.PostCount, s.Label);
            })
            .OrderBy(v => v.MeanScore)
            .ToArray();

        var caveats = BuildDataCaveats(holdings, holdingViews, settings.MinPriceFreshnessSeconds, focusTicker);

        logger?.LogInformation(
            "Assembled RunContext: {HoldingCount} holdings, {SnapshotCount} snapshots, {NewsCount} news items, {SentimentCount} sentiment tickers, {CaveatCount} caveats, MarketValue={MarketValue:C}",
            holdingViews.Count, latestSnapshots.Count, newsHeadlines.Length, sentimentViews.Length, caveats?.Count ?? 0, totals.MarketValueUsd);

        return new RunContext(
            GeneratedAtUtc: now,
            TriggerKind: trigger.Kind.ToString(),
            TriggerDetail: trigger.Detail,
            Profile: profileSnapshot,
            Totals: totals,
            Holdings: holdingViews,
            Allocation: allocation,
            TopMovers: movers,
            RecentNews: newsHeadlines,
            Sentiment: sentimentViews,
            DataCaveats: caveats);
    }

    /// <summary>
    /// Plain-language data-quality warnings the model must weigh: stale or missing prices and
    /// the FX simplification on cost basis. Null when there is nothing to caveat (the common
    /// case), so no tokens are spent on it.
    /// </summary>
    private static IReadOnlyList<string>? BuildDataCaveats(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<HoldingView> views,
        int minPriceFreshnessSeconds,
        string? focusTicker)
    {
        var caveats = new List<string>();

        var stale = views.Where(v => v.PriceIsStale).Select(v => v.Ticker).ToArray();
        if (stale.Length > 0)
            caveats.Add(
                $"Prices for {string.Join(", ", stale)} are older than {minPriceFreshnessSeconds}s " +
                "(see priceAsOfUtc); market values and allocations for those holdings may be out of date.");

        var missing = views.Where(v => v.Price is null).Select(v => v.Ticker).ToArray();
        if (missing.Length > 0)
            caveats.Add(
                $"No price is available for {string.Join(", ", missing)}; those holdings are excluded " +
                "from portfolio totals and allocation percentages.");

        if (holdings.Any(h => Currency.Normalize(h.Currency) != "USD"))
            caveats.Add(
                "Non-USD holdings are converted to USD at current FX rates, including cost basis — " +
                "unrealizedPnl excludes FX gain/loss since purchase.");

        if (focusTicker is not null)
            caveats.Add(
                $"News and sentiment in this payload are scoped to {focusTicker} (the triggering ticker) " +
                "plus market-wide items.");

        return caveats.Count > 0 ? caveats : null;
    }

    // Loads the latest snapshot per ticker regardless of age: a stale price flagged as stale is
    // more useful to the model than a holding that silently vanishes from totals. Staleness is
    // marked per holding in ComputeHoldingViews and called out in DataCaveats.
    private static async Task<Dictionary<string, PriceSnapshot>> LoadLatestSnapshotsAsync(
        InvestAdvisorDbContext db,
        IReadOnlyCollection<string> tickers,
        CancellationToken ct)
    {
        if (tickers.Count == 0)
            return new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);

        return PortfolioCalculator.LatestByTicker(await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker))
            .OrderByDescending(s => s.FetchedAtUtc)
            .ToListAsync(ct));
    }
}
