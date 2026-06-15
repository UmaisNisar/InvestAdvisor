using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Momentum;

/// <summary>Aggregate outcome of replaying the momentum rule over history. R = multiples of risk.</summary>
public sealed record MomentumBacktestSummary(
    int TotalTrades,
    int Wins,
    int Losses,
    decimal WinRatePct,
    decimal AverageR,
    decimal ExpectancyR,
    decimal ProfitFactor,
    decimal MaxDrawdownR,
    decimal AverageHoldingDays,
    DateTime? FromUtc,
    DateTime? ToUtc)
{
    /// <summary>Empty result — no qualifying trades in the sample.</summary>
    public static readonly MomentumBacktestSummary Empty =
        new(0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, null, null);

    /// <summary>
    /// The validation gate — deliberately <b>stricter</b> than the swing engine's. Breakout entries
    /// slip worse than mean-reversion fills and momentum win rates are lower, so a thin profit factor
    /// that survives gentle costs can still be break-even live. A real edge here needs a meaningful
    /// sample, positive expectancy, and a profit factor with genuine cushion (≥1.3). Below this, setups
    /// stay "paper only — not yet validated".
    /// </summary>
    public bool HasEdge(int minTrades = 50, decimal minProfitFactor = 1.3m) =>
        TotalTrades >= minTrades && ExpectancyR > 0m && ProfitFactor >= minProfitFactor;
}

/// <summary>
/// Pure walk-forward backtest of the momentum rule. For each ticker it steps bar by bar; whenever the
/// signal qualifies on the bars seen <i>so far</i>, it enters at the <b>next</b> bar's open (no
/// lookahead) and exits at the ATR stop, the reward:risk target, or after the holding window —
/// whichever comes first. Outcomes are in R (multiples of per-trade risk) so size and price level
/// don't distort the aggregate. A higher round-trip cost than the swing engine is deducted, because
/// chasing a breakout pays more slippage than fading a dip.
/// </summary>
public static class MomentumBacktester
{
    /// <summary>Round-trip cost (commission + slippage) as a fraction of entry price — higher: breakouts slip.</summary>
    private const decimal RoundTripCost = 0.0015m;

    public static MomentumBacktestSummary Run(IReadOnlyList<MomentumInput> universe, MomentumParams? parameters = null)
    {
        var p = parameters ?? MomentumParams.Default;
        var rOutcomes = new List<decimal>();
        var holdingDaysTotal = 0;
        DateTime? from = null, to = null;

        foreach (var input in universe)
            RunOne(input.Candles, p, rOutcomes, ref holdingDaysTotal, ref from, ref to);

        if (rOutcomes.Count == 0) return MomentumBacktestSummary.Empty;

        var wins = rOutcomes.Count(r => r > 0m);
        var losses = rOutcomes.Count(r => r <= 0m);
        var grossProfit = rOutcomes.Where(r => r > 0m).Sum();
        var grossLoss = -rOutcomes.Where(r => r < 0m).Sum();
        var avgR = rOutcomes.Average();

        return new MomentumBacktestSummary(
            TotalTrades: rOutcomes.Count,
            Wins: wins,
            Losses: losses,
            WinRatePct: Math.Round((decimal)wins / rOutcomes.Count * 100m, 1),
            AverageR: Math.Round(avgR, 3),
            ExpectancyR: Math.Round(avgR, 3), // expectancy per trade == mean R
            ProfitFactor: grossLoss == 0m ? (grossProfit > 0m ? 999m : 0m) : Math.Round(grossProfit / grossLoss, 2),
            MaxDrawdownR: Math.Round(MaxDrawdown(rOutcomes), 2),
            AverageHoldingDays: Math.Round((decimal)holdingDaysTotal / rOutcomes.Count, 1),
            FromUtc: from,
            ToUtc: to);
    }

    private static void RunOne(
        IReadOnlyList<Candle> candles, MomentumParams p, List<decimal> rOutcomes,
        ref int holdingDaysTotal, ref DateTime? from, ref DateTime? to)
    {
        var warmup = Math.Max(Math.Max(p.TrendSmaPeriod, p.BreakoutLookback), p.AtrPeriod) + 1;
        if (candles.Count <= warmup + 1) return;

        var i = warmup;
        while (i < candles.Count - 1) // need at least one bar after `i` to enter
        {
            var window = new ArraySegmentList(candles, i + 1); // bars [0..i]
            var built = MomentumSignalBuilder.Build(new MomentumInput("", "", "", default, window), p);
            if (built is null || !MomentumSignalBuilder.Qualifies(built.Value.Features, p))
            {
                i++;
                continue;
            }

            var atr = built.Value.Features.Atr!.Value;
            var entry = candles[i + 1].Open;
            var risk = p.StopAtrMultiple * atr;
            if (entry - risk <= 0m) { i++; continue; }

            var (exitPrice, exitIndex) = SimulateExit(candles, i + 1, entry, atr, p);

            // R, net of round-trip cost. A stop-out lands at ≈ −1R; a trailed/target exit at its level.
            var costPrice = entry * RoundTripCost;
            var r = (exitPrice - entry - costPrice) / risk;
            rOutcomes.Add(r);
            holdingDaysTotal += exitIndex - (i + 1) + 1;

            var entryTime = candles[i + 1].Time;
            var exitTime = candles[exitIndex].Time;
            if (from is null || entryTime < from) from = entryTime;
            if (to is null || exitTime > to) to = exitTime;

            i = exitIndex + 1; // no overlapping trades on the same ticker
        }
    }

    /// <summary>
    /// Walks forward from <paramref name="entryIndex"/> up to <c>HoldingDays</c> sessions, returning the
    /// exit price/index. The stop is always checked before the target/trail on the same bar
    /// (conservative). With <see cref="MomentumParams.UseTrailingStop"/> the fixed target is dropped and
    /// the stop ratchets up to a chandelier trail once the move clears the activation threshold; the
    /// trail only uses highs realized through the prior bar, so there is no intrabar lookahead.
    /// </summary>
    private static (decimal ExitPrice, int ExitIndex) SimulateExit(
        IReadOnlyList<Candle> candles, int entryIndex, decimal entry, decimal atr, MomentumParams p)
    {
        var initialStop = entry - p.StopAtrMultiple * atr;
        var risk = entry - initialStop;
        var target = entry + p.TargetAtrMultiple * atr;
        var activateAt = entry + p.TrailActivateR * risk;
        var lastIndex = Math.Min(entryIndex + p.HoldingDays - 1, candles.Count - 1);

        var effectiveStop = initialStop;
        var highest = entry; // highest high since entry, drives the trail

        for (var j = entryIndex; j <= lastIndex; j++)
        {
            if (candles[j].Low <= effectiveStop) return (effectiveStop, j);
            if (!p.UseTrailingStop && candles[j].High >= target) return (target, j);

            // Update the running high, then (once activated) ratchet the trail up for the next bar.
            if (candles[j].High > highest) highest = candles[j].High;
            if (p.UseTrailingStop && highest >= activateAt)
            {
                var trail = highest - p.TrailAtrMultiple * atr;
                if (trail > effectiveStop) effectiveStop = trail;
            }
        }
        return (candles[lastIndex].Close, lastIndex); // time-based exit
    }

    /// <summary>Largest peak-to-trough drop of the cumulative R equity curve.</summary>
    private static decimal MaxDrawdown(IReadOnlyList<decimal> rOutcomes)
    {
        decimal cum = 0m, peak = 0m, maxDd = 0m;
        foreach (var r in rOutcomes)
        {
            cum += r;
            if (cum > peak) peak = cum;
            var dd = peak - cum;
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    /// <summary>
    /// Zero-allocation view of the first <c>count</c> candles of a list, so the walk-forward loop can
    /// hand a prefix window to the shared builder without copying on every bar.
    /// </summary>
    private sealed class ArraySegmentList(IReadOnlyList<Candle> source, int count) : IReadOnlyList<Candle>
    {
        public Candle this[int index] => index < count
            ? source[index]
            : throw new ArgumentOutOfRangeException(nameof(index));
        public int Count => count;
        public IEnumerator<Candle> GetEnumerator()
        {
            for (var i = 0; i < count; i++) yield return source[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
