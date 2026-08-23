using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Portfolio;

/// <summary>
/// Pure portfolio arithmetic over holdings + latest price snapshots + FX rates. Both the
/// dashboard read model and the agent's run context are built from these so the numbers the
/// user sees and the numbers the model reasons over can never diverge. All money values are
/// USD unless named otherwise; per-holding <c>Price</c>/<c>AvgCost</c> stay in native currency.
/// </summary>
public static class PortfolioCalculator
{
    /// <summary>Newest snapshot per ticker from a newest-first sequence.</summary>
    public static Dictionary<string, PriceSnapshot> LatestByTicker(IEnumerable<PriceSnapshot> newestFirst)
    {
        var dict = new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in newestFirst)
            if (!dict.ContainsKey(s.Ticker)) dict[s.Ticker] = s;
        return dict;
    }

    public static decimal RateFor(IReadOnlyDictionary<string, decimal> rates, string? currency) =>
        rates.TryGetValue(Currency.Normalize(currency), out var r) ? r : 1m;

    /// <summary>
    /// One view per holding plus the USD market value summed over priced holdings. Allocation
    /// and drift are relative to that priced total; unpriced holdings get null for both.
    /// <paramref name="freshnessCutoff"/> marks snapshots older than it as stale;
    /// <paramref name="metricsByTicker"/> attaches screener momentum when supplied.
    /// </summary>
    public static (IReadOnlyList<HoldingView> Views, decimal TotalMarketValueUsd) HoldingViews(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates,
        DateTime? freshnessCutoff = null,
        IReadOnlyDictionary<string, StockMetric>? metricsByTicker = null)
    {
        var totalMv = 0m;
        var pre = new List<(Holding h, PriceSnapshot? snap, decimal? mv, decimal? pnl, decimal? pnlPct)>(holdings.Count);
        foreach (var h in holdings)
        {
            var rate = RateFor(rates, h.Currency);
            snapshots.TryGetValue(h.Ticker, out var snap);
            decimal? mv = snap is null ? null : h.Quantity * snap.Price * rate;
            var costUsd = h.Quantity * h.AvgCost * rate;
            decimal? pnl = mv is null ? null : mv - costUsd;
            decimal? pnlPct = (pnl is null || costUsd == 0m) ? null : (pnl / costUsd) * 100m;
            if (mv is not null) totalMv += mv.Value;
            pre.Add((h, snap, mv, pnl, pnlPct));
        }

        var views = new List<HoldingView>(pre.Count);
        foreach (var (h, snap, mv, pnl, pnlPct) in pre)
        {
            decimal? currentAlloc = (mv is null || totalMv == 0m) ? null : (mv / totalMv) * 100m;
            decimal? drift = (currentAlloc is null || h.TargetAllocationPct is null) ? null : currentAlloc - h.TargetAllocationPct;
            StockMetric? metric = null;
            metricsByTicker?.TryGetValue(h.Ticker, out metric);

            views.Add(new HoldingView(
                Ticker: h.Ticker,
                Name: h.Name,
                AssetClass: h.AssetClass.ToString(),
                AccountType: h.AccountType.ToString(),
                Quantity: h.Quantity,
                AvgCost: h.AvgCost,
                Price: snap?.Price,
                MarketValueUsd: mv,
                UnrealizedPnlUsd: pnl,
                UnrealizedPnlPct: pnlPct,
                TodaysChangePct: snap?.PercentChange,
                CurrentAllocationPct: currentAlloc,
                TargetAllocationPct: h.TargetAllocationPct,
                DriftPct: drift,
                Currency: Currency.Normalize(h.Currency),
                PriceAsOfUtc: snap?.FetchedAtUtc,
                PriceIsStale: freshnessCutoff is { } cutoff && snap is not null && snap.FetchedAtUtc < cutoff,
                MomentumShortPct: metric?.MomentumShort,
                MomentumLongPct: metric?.MomentumLong));
        }
        return (views, totalMv);
    }

    public static PortfolioTotals Totals(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates,
        decimal realizedPnlUsd = 0m)
    {
        decimal mv = 0m, cost = 0m, prev = 0m;
        foreach (var h in holdings)
        {
            var rate = RateFor(rates, h.Currency);
            cost += h.Quantity * h.AvgCost * rate;
            if (snapshots.TryGetValue(h.Ticker, out var snap))
            {
                mv += h.Quantity * snap.Price * rate;
                prev += h.Quantity * snap.PreviousClose * rate;
            }
        }
        var pnl = mv - cost;
        return new PortfolioTotals(
            MarketValueUsd: mv,
            CostBasisUsd: cost,
            UnrealizedPnlUsd: pnl,
            UnrealizedPnlPct: cost == 0m ? 0m : (pnl / cost) * 100m,
            TodaysChangeUsd: mv - prev,
            TodaysChangePct: prev == 0m ? 0m : ((mv - prev) / prev) * 100m,
            RealizedPnlUsd: realizedPnlUsd);
    }

    /// <summary>Total realized P&amp;L (Proceeds − CostBasis) across closed lots, converted to USD with current FX.</summary>
    public static decimal RealizedPnlUsd(IEnumerable<RealizedLot> lots, IReadOnlyDictionary<string, decimal> rates)
    {
        decimal sum = 0m;
        foreach (var l in lots) sum += (l.Proceeds - l.CostBasis) * RateFor(rates, l.Currency);
        return sum;
    }

    /// <summary>Percent of priced market value by asset class and by account, plus drift rows sorted by |drift|.</summary>
    public static AllocationView Allocation(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<HoldingView> views,
        decimal totalMarketValueUsd)
    {
        var byClass = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var byAcct = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var i = 0; i < holdings.Count; i++)
        {
            var v = views[i].MarketValueUsd;
            if (v is null) continue;
            var ac = holdings[i].AssetClass.ToString();
            var at = holdings[i].AccountType.ToString();
            byClass[ac] = byClass.GetValueOrDefault(ac) + v.Value;
            byAcct[at] = byAcct.GetValueOrDefault(at) + v.Value;
        }
        if (totalMarketValueUsd > 0m)
        {
            foreach (var k in byClass.Keys.ToArray()) byClass[k] = byClass[k] / totalMarketValueUsd * 100m;
            foreach (var k in byAcct.Keys.ToArray()) byAcct[k] = byAcct[k] / totalMarketValueUsd * 100m;
        }
        var drifts = views
            .Where(v => v.DriftPct is not null && v.TargetAllocationPct is not null)
            .Select(v => new DriftRow(v.Ticker, v.CurrentAllocationPct ?? 0m, v.TargetAllocationPct!.Value, v.DriftPct!.Value))
            .OrderByDescending(d => Math.Abs(d.DriftPct))
            .ToArray();
        return new AllocationView(byClass, byAcct, drifts);
    }

    /// <summary>
    /// Largest absolute daily moves. With <paramref name="freshnessCutoff"/> set, stale snapshots
    /// are excluded — a stale percent-change is a previous session's move presented as "today".
    /// </summary>
    public static IReadOnlyList<MoverView> TopMovers(
        IEnumerable<PriceSnapshot> snapshots, int count, DateTime? freshnessCutoff = null) =>
        snapshots
            .Where(s => freshnessCutoff is not { } cutoff || s.FetchedAtUtc >= cutoff)
            .OrderByDescending(s => Math.Abs(s.PercentChange))
            .Take(count)
            .Select(s => new MoverView(s.Ticker, s.Price, s.PercentChange, s.PercentChange >= 0m ? "up" : "down"))
            .ToArray();
}
