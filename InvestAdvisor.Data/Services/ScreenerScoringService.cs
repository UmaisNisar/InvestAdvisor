using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Rule-based composite scorer, asset-class-aware. Each factor is percentile-ranked across the
/// members of that class (so units/outliers don't matter), combined into 0–100 sub-scores, then a
/// weighted average over present sub-scores. Equities use six factors; ETFs use momentum + a
/// low-beta "quality" factor; crypto uses momentum + market-cap "size". The user-set factor
/// weights apply where the factor exists (momentum/quality carry over to ETFs/crypto).
/// </summary>
public sealed class ScreenerScoringService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore) : IScreenerScoringService
{
    private static readonly TimeSpan InsiderWindow = TimeSpan.FromDays(90);

    public async Task<IReadOnlyList<StockScore>> RankAsync(
        AssetClass assetClass = AssetClass.Equity, CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);
        decimal wValuation = settings.WeightValuation, wGrowth = settings.WeightGrowth,
                wQuality = settings.WeightQuality, wAnalyst = settings.WeightAnalyst,
                wInsider = settings.WeightInsider, wMomentum = settings.WeightMomentum;
        if (wValuation + wGrowth + wQuality + wAnalyst + wInsider + wMomentum <= 0m)
            (wValuation, wGrowth, wQuality, wAnalyst, wInsider, wMomentum) = (20m, 25m, 10m, 20m, 10m, 15m);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var stocks = await db.Stocks.AsNoTracking()
            .Where(s => s.IsActive && s.AssetClass == assetClass)
            .Select(s => new { s.Ticker, s.Name, s.Sector })
            .ToListAsync(ct);
        if (stocks.Count == 0) return Array.Empty<StockScore>();

        var tickers = stocks.Select(s => s.Ticker).ToList();
        var metrics = (await db.StockMetrics.AsNoTracking()
                .Where(m => tickers.Contains(m.Ticker)).ToListAsync(ct))
            .GroupBy(m => m.Ticker)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.FetchedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<Core.Entities.AnalystRating>> ratingsByTicker = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, decimal> insiderNet = new(StringComparer.OrdinalIgnoreCase);
        if (assetClass == AssetClass.Equity)
        {
            ratingsByTicker = (await db.AnalystRatings.AsNoTracking()
                    .Where(a => tickers.Contains(a.Ticker)).ToListAsync(ct))
                .GroupBy(a => a.Ticker)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Period, StringComparer.Ordinal).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var insiderCutoff = DateTime.UtcNow - InsiderWindow;
            insiderNet = (await db.InsiderTrades.AsNoTracking()
                    .Where(t => tickers.Contains(t.Ticker) && t.FilingDate >= insiderCutoff)
                    .GroupBy(t => t.Ticker)
                    .Select(g => new { Ticker = g.Key, Net = g.Sum(x => x.Change) })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Ticker, x => x.Net, StringComparer.OrdinalIgnoreCase);
        }

        var rows = new List<Raw>(stocks.Count);
        foreach (var s in stocks)
        {
            metrics.TryGetValue(s.Ticker, out var m);
            ratingsByTicker.TryGetValue(s.Ticker, out var ratings);

            decimal? buyPct = null;
            int analystTotal = 0, buyCount = 0;
            int? trendDelta = null;
            if (ratings is { Count: > 0 })
            {
                var latest = ratings[0];
                analystTotal = latest.StrongBuy + latest.Buy + latest.Hold + latest.Sell + latest.StrongSell;
                buyCount = latest.StrongBuy + latest.Buy;
                if (analystTotal > 0) buyPct = (decimal)buyCount / analystTotal * 100m;
                if (ratings.Count > 1) trendDelta = buyCount - (ratings[1].StrongBuy + ratings[1].Buy);
            }

            rows.Add(new Raw
            {
                Ticker = s.Ticker, Name = s.Name, Sector = s.Sector,
                Pe = m?.PeRatio, Pfcf = m?.PriceToFreeCashFlow,
                Rev = m?.RevenueGrowthPct, Eps = m?.EpsGrowthPct, De = m?.DebtToEquity,
                MomShort = m?.MomentumShort, MomLong = m?.MomentumLong,
                Beta = m?.Beta, MarketCap = m?.MarketCap,
                BuyPct = buyPct, AnalystTotal = analystTotal, BuyCount = buyCount, TrendDelta = trendDelta,
                NetInsider = insiderNet.TryGetValue(s.Ticker, out var net) ? net : null,
                DataAsOfUtc = m?.FetchedAtUtc,
            });
        }

        // Percentile ranks (shared)
        var rMomS = Rank(rows, r => r.MomShort, higherBetter: true);
        var rMomL = Rank(rows, r => r.MomLong, higherBetter: true);

        var scores = new List<StockScore>(rows.Count);

        if (assetClass == AssetClass.Equity)
        {
            var rPe = Rank(rows, r => r.Pe, false);
            var rPfcf = Rank(rows, r => r.Pfcf, false);
            var rRev = Rank(rows, r => r.Rev, true);
            var rEps = Rank(rows, r => r.Eps, true);
            var rDe = Rank(rows, r => r.De, false);
            var rBuy = Rank(rows, r => r.BuyPct, true);
            var rTrend = Rank(rows, r => r.TrendDelta.HasValue ? r.TrendDelta.Value : (decimal?)null, true);
            var rInsider = Rank(rows, r => r.NetInsider, true);

            foreach (var r in rows)
            {
                decimal? sV = Scale(Avg(Get(rPe, r.Ticker), Get(rPfcf, r.Ticker)));
                decimal? sG = Scale(Avg(Get(rRev, r.Ticker), Get(rEps, r.Ticker)));
                decimal? sQ = Scale(Get(rDe, r.Ticker));
                decimal? sA = Scale(Avg(Get(rBuy, r.Ticker), Get(rTrend, r.Ticker)));
                decimal? sI = Scale(Get(rInsider, r.Ticker));
                decimal? sM = Scale(Avg(Get(rMomS, r.Ticker), Get(rMomL, r.Ticker)));
                var composite = Composite(
                    (sV, wValuation), (sG, wGrowth), (sQ, wQuality), (sA, wAnalyst), (sI, wInsider), (sM, wMomentum));
                scores.Add(MakeScore(r, composite, new FactorScores(sV, sG, sQ, sA, sI, sM)));
            }
        }
        else if (assetClass == AssetClass.Etf)
        {
            var rBeta = Rank(rows, r => r.Beta, higherBetter: false); // lower beta = steadier
            foreach (var r in rows)
            {
                decimal? sM = Scale(Avg(Get(rMomS, r.Ticker), Get(rMomL, r.Ticker)));
                decimal? sQ = Scale(Get(rBeta, r.Ticker));
                var composite = Composite((sM, wMomentum), (sQ, wQuality));
                scores.Add(MakeScore(r, composite, new FactorScores(null, null, sQ, null, null, sM)));
            }
        }
        else // Crypto
        {
            var rSize = Rank(rows, r => r.MarketCap, higherBetter: true); // bigger = more established
            foreach (var r in rows)
            {
                decimal? sM = Scale(Avg(Get(rMomS, r.Ticker), Get(rMomL, r.Ticker)));
                decimal? sQ = Scale(Get(rSize, r.Ticker));
                var composite = Composite((sM, wMomentum), (sQ, wQuality));
                scores.Add(MakeScore(r, composite, new FactorScores(null, null, sQ, null, null, sM)));
            }
        }

        return scores
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    private static StockScore MakeScore(Raw r, decimal composite, FactorScores factors) =>
        new(r.Ticker, r.Name, r.Sector, composite, factors,
            new StockSnapshot(r.Pe, r.Pfcf, r.Rev, r.Eps, r.De, r.MomShort,
                r.AnalystTotal, r.BuyCount, r.BuyPct ?? 0m, r.TrendDelta ?? 0,
                r.NetInsider ?? 0m, r.DataAsOfUtc));

    private static decimal Composite(params (decimal? Score, decimal Weight)[] parts)
    {
        decimal acc = 0m, wsum = 0m;
        foreach (var (sc, w) in parts)
            if (sc.HasValue) { acc += sc.Value * w; wsum += w; }
        return wsum > 0m ? Math.Round(acc / wsum, 1) : 0m;
    }

    private sealed class Raw
    {
        public string Ticker = "", Name = "", Sector = "";
        public decimal? Pe, Pfcf, Rev, Eps, De, MomShort, MomLong, Beta, MarketCap, BuyPct, NetInsider;
        public int AnalystTotal, BuyCount;
        public int? TrendDelta;
        public DateTime? DataAsOfUtc;
    }

    private static decimal? Scale(decimal? rank01) => rank01.HasValue ? Math.Round(rank01.Value * 100m, 1) : null;
    private static decimal? Get(Dictionary<string, decimal> d, string t) => d.TryGetValue(t, out var v) ? v : null;

    private static decimal? Avg(params decimal?[] vals)
    {
        var present = vals.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Average();
    }

    private static Dictionary<string, decimal> Rank(List<Raw> rows, Func<Raw, decimal?> sel, bool higherBetter)
    {
        var present = rows.Select(r => (r.Ticker, V: sel(r)))
            .Where(x => x.V.HasValue).Select(x => (x.Ticker, V: x.V!.Value)).ToList();
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var n = present.Count;
        if (n == 0) return result;
        if (n == 1) { result[present[0].Ticker] = 0.5m; return result; }
        foreach (var (ticker, v) in present)
        {
            var better = present.Count(p => higherBetter ? p.V < v : p.V > v);
            var equal = present.Count(p => p.V == v);
            var rank = (better + (equal - 1) / 2m) / (n - 1);
            result[ticker] = Math.Clamp(rank, 0m, 1m);
        }
        return result;
    }
}
