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

        // Only this user's tracked names plus market-wide (null-ticker) items. The same table
        // also holds screener-universe social posts, which would otherwise crowd out the
        // holdings' actual news within the item cap.
        var news = await db.NewsItems.AsNoTracking()
            .Where(n => n.FetchedAtUtc >= newsCutoff
                        && (n.Ticker == null || newsScope.Contains(n.Ticker)))
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
        foreach (var c in holdings.Select(h => Cur(h.Currency)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);

        var holdingViews = ComputeHoldingViews(holdings, latestSnapshots, rates, freshnessCutoff, metricsByTicker);
        var totals = ComputePortfolioTotals(holdings, latestSnapshots, rates);
        var allocation = ComputeAllocation(holdings, holdingViews, totals.MarketValueUsd);

        var movers = ComputeTopMovers(latestSnapshots, freshnessCutoff);

        var newsHeadlines = news.Select(n => new NewsHeadline(
            Ticker: n.Ticker,
            Headline: Truncate(n.Headline, MaxHeadlineLength),
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

        if (holdings.Any(h => Cur(h.Currency) != "USD"))
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

        var candidate = await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker))
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
        IReadOnlyDictionary<string, decimal> rates,
        DateTime freshnessCutoff,
        IReadOnlyDictionary<string, StockMetric> metricsByTicker)
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

            snapshots.TryGetValue(h.Ticker, out var snap);
            metricsByTicker.TryGetValue(h.Ticker, out var metric);

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
                Currency: Cur(h.Currency),
                PriceAsOfUtc: snap?.FetchedAtUtc,
                PriceIsStale: snap is not null && snap.FetchedAtUtc < freshnessCutoff,
                MomentumShortPct: metric?.MomentumShort,
                MomentumLongPct: metric?.MomentumLong));
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

    // Movers only consider fresh snapshots: a stale percent-change is a previous session's move
    // and would be presented as "today".
    private static IReadOnlyList<MoverView> ComputeTopMovers(
        IReadOnlyDictionary<string, PriceSnapshot> snapshots, DateTime freshnessCutoff)
    {
        return snapshots.Values
            .Where(s => s.FetchedAtUtc >= freshnessCutoff)
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
