using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
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

        var latestSnapshots = await LoadLatestSnapshotsAsync(db, trackedTickers, freshnessCutoff, ct);

        // News by run type. Scheduled + Manual runs are the review/discovery runs, so they get the
        // full broad feed (the "where to invest next" signal, including names outside the portfolio).
        // Condition triggers (big move / price target / drift) are event-focused and frequent — they
        // only need news about the affected positions, so scope those to the user's tracked tickers.
        var isCondition = trigger.Kind is RunTriggerKind.PriceTarget
            or RunTriggerKind.BigMove or RunTriggerKind.DriftThreshold;
        var newsQuery = db.NewsItems.AsNoTracking().Where(n => n.FetchedAtUtc >= newsCutoff);
        if (isCondition)
            newsQuery = newsQuery.Where(n => trackedTickers.Contains(n.Ticker));
        var news = await newsQuery
            .OrderByDescending(n => n.PublishedAtUtc)
            .Take(MaxNewsItems)
            .ToListAsync(ct);

        var profileSnapshot = new ProfileSnapshot(
            GoalsText: profile.GoalsText,
            RiskTolerance: profile.RiskTolerance.ToString(),
            TimeHorizon: profile.TimeHorizon.ToString(),
            DriftPctThreshold: profile.DriftPctThreshold,
            SingleDayMovePctThreshold: profile.SingleDayMovePctThreshold,
            RebalanceCadenceHours: profile.RebalanceCadenceHours);

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        foreach (var c in holdings.Select(h => Cur(h.Currency)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);

        var holdingViews = ComputeHoldingViews(holdings, latestSnapshots, rates);
        var totals = ComputePortfolioTotals(holdings, latestSnapshots, rates);
        var allocation = ComputeAllocation(holdings, holdingViews, totals.MarketValueUsd);

        var movers = ComputeTopMovers(latestSnapshots);

        var newsHeadlines = news.Select(n => new NewsHeadline(
            Ticker: n.Ticker,
            Headline: Truncate(n.Headline, MaxHeadlineLength),
            Source: n.Source,
            Url: n.Url,
            PublishedAtUtc: n.PublishedAtUtc)).ToArray();

        logger?.LogInformation(
            "Assembled RunContext: {HoldingCount} holdings, {SnapshotCount} fresh snapshots, {NewsCount} news items, MarketValue={MarketValue:C}",
            holdingViews.Count, latestSnapshots.Count, newsHeadlines.Length, totals.MarketValueUsd);

        return new RunContext(
            GeneratedAtUtc: now,
            TriggerKind: trigger.Kind.ToString(),
            TriggerDetail: trigger.Detail,
            Profile: profileSnapshot,
            Totals: totals,
            Holdings: holdingViews,
            Allocation: allocation,
            TopMovers: movers,
            RecentNews: newsHeadlines);
    }

    private static async Task<Dictionary<string, PriceSnapshot>> LoadLatestSnapshotsAsync(
        InvestAdvisorDbContext db,
        IReadOnlyCollection<string> tickers,
        DateTime freshnessCutoff,
        CancellationToken ct)
    {
        if (tickers.Count == 0)
            return new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);

        var candidate = await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker) && s.FetchedAtUtc >= freshnessCutoff)
            .OrderByDescending(s => s.FetchedAtUtc)
            .ToListAsync(ct);

        var dict = new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in candidate)
        {
            if (!dict.ContainsKey(s.Ticker))
                dict[s.Ticker] = s;
        }
        return dict;
    }

    private static string Cur(string? c) => string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant();

    private static IReadOnlyList<HoldingView> ComputeHoldingViews(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates)
    {
        var totalMarketValue = 0m;
        var perHolding = new List<(Holding h, decimal? mv, decimal? price, decimal? pnl, decimal? pnlPct, decimal? todayPct)>();

        foreach (var h in holdings)
        {
            var rate = rates.TryGetValue(Cur(h.Currency), out var r) ? r : 1m;
            snapshots.TryGetValue(h.Ticker, out var snap);
            decimal? price = snap?.Price; // native currency
            decimal? mv = price is null ? null : h.Quantity * price.Value * rate; // USD
            decimal costUsd = h.Quantity * h.AvgCost * rate;
            decimal? pnl = mv is null ? null : mv - costUsd;
            decimal? pnlPct = (pnl is null || costUsd == 0m) ? null : (pnl / costUsd) * 100m;
            decimal? todayPct = snap?.PercentChange;
            if (mv is not null) totalMarketValue += mv.Value;
            perHolding.Add((h, mv, price, pnl, pnlPct, todayPct));
        }

        var views = new List<HoldingView>(perHolding.Count);
        foreach (var (h, mv, price, pnl, pnlPct, todayPct) in perHolding)
        {
            decimal? currentAlloc = (mv is null || totalMarketValue == 0m) ? null : (mv / totalMarketValue) * 100m;
            decimal? drift = (currentAlloc is null || h.TargetAllocationPct is null)
                ? null
                : currentAlloc - h.TargetAllocationPct;

            views.Add(new HoldingView(
                Ticker: h.Ticker,
                Name: h.Name,
                AssetClass: h.AssetClass.ToString(),
                AccountType: h.AccountType.ToString(),
                Quantity: h.Quantity,
                AvgCost: h.AvgCost,
                Price: price,
                MarketValueUsd: mv,
                UnrealizedPnlUsd: pnl,
                UnrealizedPnlPct: pnlPct,
                TodaysChangePct: todayPct,
                CurrentAllocationPct: currentAlloc,
                TargetAllocationPct: h.TargetAllocationPct,
                DriftPct: drift,
                Currency: Cur(h.Currency)));
        }
        return views;
    }

    private static PortfolioTotals ComputePortfolioTotals(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates)
    {
        decimal marketValue = 0m;
        decimal costBasis = 0m;
        decimal previousValue = 0m;

        foreach (var h in holdings)
        {
            var rate = rates.TryGetValue(Cur(h.Currency), out var r) ? r : 1m;
            costBasis += h.Quantity * h.AvgCost * rate;
            if (snapshots.TryGetValue(h.Ticker, out var snap))
            {
                marketValue += h.Quantity * snap.Price * rate;
                previousValue += h.Quantity * snap.PreviousClose * rate;
            }
        }

        var unrealizedPnl = marketValue - costBasis;
        var unrealizedPnlPct = costBasis == 0m ? 0m : (unrealizedPnl / costBasis) * 100m;
        var todaysChangeUsd = marketValue - previousValue;
        var todaysChangePct = previousValue == 0m ? 0m : (todaysChangeUsd / previousValue) * 100m;

        return new PortfolioTotals(
            MarketValueUsd: marketValue,
            CostBasisUsd: costBasis,
            UnrealizedPnlUsd: unrealizedPnl,
            UnrealizedPnlPct: unrealizedPnlPct,
            TodaysChangeUsd: todaysChangeUsd,
            TodaysChangePct: todaysChangePct);
    }

    private static AllocationView ComputeAllocation(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<HoldingView> views,
        decimal totalMarketValue)
    {
        var byAssetClass = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var byAccountType = new Dictionary<string, decimal>(StringComparer.Ordinal);

        for (var i = 0; i < holdings.Count; i++)
        {
            var mv = views[i].MarketValueUsd;
            if (mv is null) continue;
            var ac = holdings[i].AssetClass.ToString();
            var at = holdings[i].AccountType.ToString();
            byAssetClass[ac] = byAssetClass.GetValueOrDefault(ac) + mv.Value;
            byAccountType[at] = byAccountType.GetValueOrDefault(at) + mv.Value;
        }

        if (totalMarketValue > 0m)
        {
            foreach (var k in byAssetClass.Keys.ToArray())
                byAssetClass[k] = (byAssetClass[k] / totalMarketValue) * 100m;
            foreach (var k in byAccountType.Keys.ToArray())
                byAccountType[k] = (byAccountType[k] / totalMarketValue) * 100m;
        }

        var drifts = views
            .Where(v => v.DriftPct is not null && v.TargetAllocationPct is not null)
            .Select(v => new DriftRow(v.Ticker, v.CurrentAllocationPct ?? 0m, v.TargetAllocationPct!.Value, v.DriftPct!.Value))
            .OrderByDescending(d => Math.Abs(d.DriftPct))
            .ToArray();

        return new AllocationView(byAssetClass, byAccountType, drifts);
    }

    private static IReadOnlyList<MoverView> ComputeTopMovers(IReadOnlyDictionary<string, PriceSnapshot> snapshots)
    {
        return snapshots.Values
            .OrderByDescending(s => Math.Abs(s.PercentChange))
            .Take(TopMoversCount)
            .Select(s => new MoverView(
                Ticker: s.Ticker,
                Price: s.Price,
                PercentChange: s.PercentChange,
                Direction: s.PercentChange >= 0m ? "up" : "down"))
            .ToArray();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
