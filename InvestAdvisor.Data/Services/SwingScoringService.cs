using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Swing;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Technical, short-horizon counterpart to <see cref="ScreenerScoringService"/>. Each factor is
/// percentile-ranked across the swing universe (so absolute units don't matter), combined into
/// 0–100 sub-scores, then weighted into a composite. Names below the liquidity floor are dropped
/// before ranking — that's the gate that keeps thin TSX-V venture tickers out. Pure: no I/O.
/// </summary>
public sealed class SwingScoringService : ISwingScoringService
{
    // Sub-score weights (sum 100) for the mean-reversion thesis: the oversold trigger and the
    // up-trend regime lead; pullback depth and a volume/sentiment confirm trim.
    private const decimal WOversold = 30m, WRegime = 25m, WPullback = 20m, WVolume = 10m, WSentiment = 15m;

    public IReadOnlyList<SwingScore> Rank(
        IReadOnlyList<SwingInput> universe,
        IReadOnlyDictionary<string, decimal>? sentimentByTicker = null,
        SwingParams? parameters = null)
    {
        var p = parameters ?? SwingParams.Default;
        var rows = new List<Row>(universe.Count);

        foreach (var input in universe)
        {
            var built = SwingSignalBuilder.Build(input, p);
            if (built is null) continue;
            var (features, setup) = built.Value;

            // Liquidity gate: skip names that can't be traded cleanly on a 2–3 day horizon.
            if (features.AverageDollarVolume is not { } adv || adv < p.MinAvgDollarVolume) continue;

            rows.Add(new Row(input, features, setup,
                Sentiment: sentimentByTicker is not null && sentimentByTicker.TryGetValue(input.Ticker, out var sv) ? sv : null));
        }

        if (rows.Count == 0) return Array.Empty<SwingScore>();

        // Oversold: a lower RSI is a better mean-reversion entry, so rank it lower-better.
        var rOversold = Rank(rows, r => r.Features.Rsi, higherBetter: false);
        // Regime: more above the 200-day SMA = a healthier up-trend to fade the dip into.
        var rRegime = Rank(rows, r => r.Features.RegimeDistancePct, higherBetter: true);
        // Pullback depth: a deeper short-term dip has more to revert.
        var rPullback = Rank(rows, r => r.Features.PullbackPct, higherBetter: true);
        var rVol = Rank(rows, r => r.Features.RelativeVolume, higherBetter: true);
        var rSent = Rank(rows, r => r.Sentiment, higherBetter: true);

        var scores = new List<SwingScore>(rows.Count);
        foreach (var r in rows)
        {
            decimal? sOversold = Scale(Get(rOversold, r.Input.Ticker));
            decimal? sRegime = Scale(Get(rRegime, r.Input.Ticker));
            decimal? sPullback = Scale(Get(rPullback, r.Input.Ticker));
            decimal? sVol = Scale(Get(rVol, r.Input.Ticker));
            decimal? sSent = Scale(Get(rSent, r.Input.Ticker));

            var composite = Composite(
                (sOversold, WOversold), (sRegime, WRegime), (sPullback, WPullback),
                (sVol, WVolume), (sSent, WSentiment));
            if (composite is null) continue;

            scores.Add(new SwingScore(
                r.Input.Ticker, r.Input.Name, r.Input.Sector, composite.Value,
                new SwingFactorScores(sRegime, sOversold, sPullback, sVol, sSent),
                r.Features, r.Setup, SwingSignalBuilder.Qualifies(r.Features, p)));
        }

        return scores
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record Row(SwingInput Input, SwingFeatures Features, TradeSetup Setup, decimal? Sentiment);

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
