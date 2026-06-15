using InvestAdvisor.Core.Abstractions;

namespace InvestAdvisor.Core.Momentum;

/// <summary>
/// Ranks a universe of names for the high-volatility breakout thesis. Each factor is percentile-ranked
/// across the universe (so absolute units don't matter), combined into 0–100 sub-scores, then weighted
/// into a composite. Names below the liquidity floor are dropped before ranking — the gate that keeps
/// untradeable thin tickers out. Pure: no I/O.
/// </summary>
public sealed class MomentumScoringService : IMomentumScoringService
{
    // Sub-score weights (sum 100): the breakout and the squeeze (tight base) lead the thesis; volume
    // and trailing momentum confirm; volatility keeps the 10%-reachable bias; sentiment trims.
    private const decimal WBreakout = 25m, WSqueeze = 20m, WVolume = 15m,
                          WVolatility = 15m, WMomentum = 15m, WSentiment = 10m;

    public IReadOnlyList<MomentumScore> Rank(
        IReadOnlyList<MomentumInput> universe,
        IReadOnlyDictionary<string, decimal>? sentimentByTicker = null,
        MomentumParams? parameters = null)
    {
        var p = parameters ?? MomentumParams.Default;
        var rows = new List<Row>(universe.Count);

        foreach (var input in universe)
        {
            var built = MomentumSignalBuilder.Build(input, p);
            if (built is null) continue;
            var (features, setup) = built.Value;

            // Liquidity gate: skip names that can't be filled cleanly on a 3-day horizon.
            if (features.AverageDollarVolume is not { } adv || adv < p.MinAvgDollarVolume) continue;

            rows.Add(new Row(input, features, setup,
                Sentiment: sentimentByTicker is not null && sentimentByTicker.TryGetValue(input.Ticker, out var sv) ? sv : null));
        }

        if (rows.Count == 0) return Array.Empty<MomentumScore>();

        // Breakout: a stronger break of the prior range ranks higher.
        var rBreakout = Rank(rows, r => r.Features.BreakoutStrength, higherBetter: true);
        // Squeeze: a tighter base (fewer ATRs of range) is a better coil, so rank it lower-better.
        // ATR-relative so high-vol names aren't unfairly penalised for their natural width.
        var rSqueeze = Rank(rows, r => r.Features.BaseRangeAtr, higherBetter: false);
        var rVolume = Rank(rows, r => r.Features.RelativeVolume, higherBetter: true);
        var rVolatility = Rank(rows, r => r.Features.AtrPercent, higherBetter: true);
        var rMomentum = Rank(rows, r => r.Features.MomentumReturn, higherBetter: true);
        var rSent = Rank(rows, r => r.Sentiment, higherBetter: true);

        var scores = new List<MomentumScore>(rows.Count);
        foreach (var r in rows)
        {
            decimal? sBreakout = Scale(Get(rBreakout, r.Input.Ticker));
            decimal? sSqueeze = Scale(Get(rSqueeze, r.Input.Ticker));
            decimal? sVolume = Scale(Get(rVolume, r.Input.Ticker));
            decimal? sVolatility = Scale(Get(rVolatility, r.Input.Ticker));
            decimal? sMomentum = Scale(Get(rMomentum, r.Input.Ticker));
            decimal? sSent = Scale(Get(rSent, r.Input.Ticker));

            var composite = Composite(
                (sBreakout, WBreakout), (sSqueeze, WSqueeze), (sVolume, WVolume),
                (sVolatility, WVolatility), (sMomentum, WMomentum), (sSent, WSentiment));
            if (composite is null) continue;

            scores.Add(new MomentumScore(
                r.Input.Ticker, r.Input.Name, r.Input.Sector, composite.Value,
                new MomentumFactorScores(sBreakout, sSqueeze, sVolume, sVolatility, sMomentum, sSent),
                r.Features, r.Setup, MomentumSignalBuilder.Qualifies(r.Features, p)));
        }

        return scores
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record Row(MomentumInput Input, MomentumFeatures Features, MomentumSetup Setup, decimal? Sentiment);

    private static decimal? Composite(params (decimal? Score, decimal Weight)[] parts)
    {
        decimal acc = 0m, wsum = 0m;
        foreach (var (sc, w) in parts)
            if (sc.HasValue) { acc += sc.Value * w; wsum += w; }
        return wsum > 0m ? Math.Round(acc / wsum, 1) : null;
    }

    private static decimal? Scale(decimal? rank01) => rank01.HasValue ? Math.Round(rank01.Value * 100m, 1) : null;
    private static decimal? Get(Dictionary<string, decimal> d, string t) => d.TryGetValue(t, out var v) ? v : null;

    private static Dictionary<string, decimal> Rank(List<Row> rows, Func<Row, decimal?> sel, bool higherBetter)
    {
        var present = rows.Select(r => (r.Input.Ticker, V: sel(r)))
            .Where(x => x.V.HasValue).Select(x => (x.Ticker, V: x.V!.Value)).ToList();
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var n = present.Count;
        if (n == 0) return result;
        if (n == 1) { result[present[0].Ticker] = 0.5m; return result; }
        foreach (var (ticker, v) in present)
        {
            var better = present.Count(x => higherBetter ? x.V < v : x.V > v);
            var equal = present.Count(x => x.V == v);
            var rank = (better + (equal - 1) / 2m) / (n - 1);
            result[ticker] = Math.Clamp(rank, 0m, 1m);
        }
        return result;
    }
}
