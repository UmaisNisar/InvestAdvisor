using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Scoring;

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
            var t = r.Input.Ticker;
            decimal? sBreakout = PercentileRanker.SubScore(rBreakout, t);
            decimal? sSqueeze = PercentileRanker.SubScore(rSqueeze, t);
            decimal? sVolume = PercentileRanker.SubScore(rVolume, t);
            decimal? sVolatility = PercentileRanker.SubScore(rVolatility, t);
            decimal? sMomentum = PercentileRanker.SubScore(rMomentum, t);
            decimal? sSent = PercentileRanker.SubScore(rSent, t);

            var composite = PercentileRanker.Composite(
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

    private static Dictionary<string, decimal> Rank(List<Row> rows, Func<Row, decimal?> sel, bool higherBetter) =>
        PercentileRanker.Rank(rows, r => r.Input.Ticker, sel, higherBetter);
}
