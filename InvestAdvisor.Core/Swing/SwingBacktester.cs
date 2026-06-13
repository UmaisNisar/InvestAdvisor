using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>Aggregate outcome of replaying the swing rule over history. R = multiples of risk.</summary>
public sealed record SwingBacktestSummary(
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
    public static readonly SwingBacktestSummary Empty =
        new(0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, null, null);

    /// <summary>
    /// The validation gate: a positive edge needs a meaningful sample <i>and</i> positive expectancy.
    /// Below this, live setups must be labelled "unvalidated — paper only".
    /// </summary>
    public bool HasEdge(int minTrades = 30) => TotalTrades >= minTrades && ExpectancyR > 0m;
}

/// <summary>
/// Pure walk-forward backtest of the swing rule. For each ticker it steps bar by bar; whenever the
/// signal qualifies on the bars seen <i>so far</i>, it enters at the <b>next</b> bar's open (no
/// lookahead) and exits at the ATR stop, the reward:risk target, or after the holding window —
/// whichever comes first. Outcomes are expressed in R (multiples of the per-trade risk) so position
/// size and price level don't distort the aggregate. A modest round-trip cost is deducted so the
/// edge has to clear real-world friction.
/// </summary>
public static class SwingBacktester
{
    /// <summary>Round-trip cost (commission + slippage) as a fraction of entry price.</summary>
    private const decimal RoundTripCost = 0.001m;

    public static SwingBacktestSummary Run(IReadOnlyList<SwingInput> universe, SwingParams? parameters = null)
    {
        var p = parameters ?? SwingParams.Default;
        var rOutcomes = new List<decimal>();
        var holdingDaysTotal = 0;
        DateTime? from = null, to = null;

        foreach (var input in universe)
            RunOne(input.Candles, p, rOutcomes, ref holdingDaysTotal, ref from, ref to);

        if (rOutcomes.Count == 0) return SwingBacktestSummary.Empty;

        var wins = rOutcomes.Count(r => r > 0m);
        var losses = rOutcomes.Count(r => r <= 0m);
        var grossProfit = rOutcomes.Where(r => r > 0m).Sum();
        var grossLoss = -rOutcomes.Where(r => r < 0m).Sum();
        var avgR = rOutcomes.Average();

        return new SwingBacktestSummary(
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
        IReadOnlyList<Candle> candles, SwingParams p, List<decimal> rOutcomes,
        ref int holdingDaysTotal, ref DateTime? from, ref DateTime? to)
    {
        var warmup = Math.Max(Math.Max(p.EmaSlowPeriod, p.AtrPeriod), Math.Max(p.BreakoutLookback, p.MomentumSessions)) + 1;
        if (candles.Count <= warmup + 1) return;

        var i = warmup;
        while (i < candles.Count - 1) // need at least one bar after `i` to enter
        {
            var window = new ArraySegmentList(candles, i + 1); // bars [0..i]
            var built = SwingSignalBuilder.Build(new SwingInput("", "", "", default, window), p);
            if (built is null || !SwingSignalBuilder.Qualifies(built.Value.Features))
            {
                i++;
                continue;
            }

            var atr = built.Value.Features.Atr!.Value;
            var entry = candles[i + 1].Open;
            var stop = entry - p.AtrStopMultiple * atr;
            if (stop <= 0m) { i++; continue; }
            var risk = entry - stop;
            var target = entry + p.RewardRiskRatio * risk;

            var (exitPrice, exitIndex, hitStop) = SimulateExit(candles, i + 1, stop, target, p.HoldingDays);

            // R, net of round-trip cost. Stop-first assumption on ambiguous bars keeps it conservative.
            var costPrice = entry * RoundTripCost;
            var r = hitStop && exitPrice == stop ? -1m - costPrice / risk
                                                 : (exitPrice - entry - costPrice) / risk;
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
    /// Walks forward from <paramref name="entryIndex"/> up to <paramref name="holdingDays"/> sessions,
    /// returning the exit price/index. Stop is checked before target on the same bar (conservative).
    /// </summary>
    private static (decimal ExitPrice, int ExitIndex, bool HitStop) SimulateExit(
        IReadOnlyList<Candle> candles, int entryIndex, decimal stop, decimal target, int holdingDays)
    {
        var lastIndex = Math.Min(entryIndex + holdingDays - 1, candles.Count - 1);
        for (var j = entryIndex; j <= lastIndex; j++)
        {
            if (candles[j].Low <= stop) return (stop, j, true);
            if (candles[j].High >= target) return (target, j, false);
        }
        return (candles[lastIndex].Close, lastIndex, false); // time-based exit
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
    /// Zero-allocation view of the first <c>count</c> candles of a list, so the walk-forward loop
    /// can hand a prefix window to the shared builder without copying on every bar.
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
