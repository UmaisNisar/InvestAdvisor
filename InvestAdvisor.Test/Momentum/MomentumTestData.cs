using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Test.Momentum;

/// <summary>Builders for synthetic candle series with known shapes, used across the momentum tests.</summary>
internal static class MomentumTestData
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A bar built from a close, with a fixed-fraction high/low band and given volume.</summary>
    public static Candle Bar(int dayIndex, decimal close, decimal bandPct = 0.04m, long volume = 1_000_000) =>
        new(Start.AddDays(dayIndex), close, close * (1 + bandPct), close * (1 - bandPct), close, volume);

    /// <summary>Flat series — every close identical (zero volatility, zero ATR).</summary>
    public static IReadOnlyList<Candle> Flat(int count, decimal price = 100m, long volume = 1_000_000)
    {
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++) list.Add(new Candle(Start.AddDays(i), price, price, price, price, volume));
        return list;
    }

    /// <summary>
    /// A high-volatility up-trend that coils into a tight base, then breaks the prior high on a volume
    /// surge — the canonical squeeze-breakout the engine is built to catch. The base is exactly
    /// <paramref name="baseBars"/> long so the (default-10) consolidation window sees only the coil.
    /// </summary>
    public static IReadOnlyList<Candle> HighVolSqueezeBreakout(
        int trendBars = 60, decimal start = 100m, decimal trendStep = 0.8m, decimal trendBand = 0.04m,
        int baseBars = 10, decimal baseBand = 0.012m, decimal breakoutPct = 0.16m,
        long baseVolume = 1_000_000, long breakoutVolume = 3_000_000)
    {
        var list = new List<Candle>();
        var day = 0;
        var price = start;
        for (var i = 0; i < trendBars; i++) { list.Add(Bar(day++, price, trendBand, baseVolume)); price += trendStep; }

        var level = price; // the tight base coils here, just under the prior highs
        for (var i = 0; i < baseBars; i++) list.Add(Bar(day++, level, baseBand, baseVolume));

        var bClose = level * (1 + breakoutPct);
        list.Add(new Candle(Start.AddDays(day), level, bClose * 1.005m, level * 0.995m, bClose, breakoutVolume));
        return list;
    }

    /// <summary>
    /// A long high-volatility up-trend made of repeated [tight base → breakout → step up] segments, so
    /// the walk-forward backtest finds many squeeze-breakout entries.
    /// </summary>
    public static IReadOnlyList<Candle> RepeatedBreakouts(
        int segments = 40, decimal start = 100m, decimal trendBand = 0.04m,
        int baseBars = 10, decimal baseBand = 0.012m, decimal breakoutPct = 0.16m,
        long baseVolume = 1_000_000, long breakoutVolume = 3_000_000)
    {
        var list = new List<Candle>();
        var day = 0;
        var level = start;

        // Warm-up trend so the 50-SMA / 20-day-high windows are populated before the first breakout.
        for (var i = 0; i < 60; i++) { list.Add(Bar(day++, level, trendBand, baseVolume)); level += 0.8m; }

        for (var s = 0; s < segments; s++)
        {
            for (var i = 0; i < baseBars; i++) list.Add(Bar(day++, level, baseBand, baseVolume));
            var bClose = level * (1 + breakoutPct);
            list.Add(new Candle(Start.AddDays(day++), level, bClose * 1.005m, level * 0.995m, bClose, breakoutVolume));
            // A couple of digestion bars at the new level before the next coil.
            level = bClose;
            list.Add(Bar(day++, level, trendBand, baseVolume));
            list.Add(Bar(day++, level, trendBand, baseVolume));
        }
        return list;
    }

    /// <summary>
    /// A squeeze breakout followed by a strong multi-day run, then a reversal — so the trade enters at
    /// the breakout and the move continues well past a fixed ATR target. Used to show the trailing exit
    /// rides the run (and out-earns the capped fixed-target exit), then stops out on the reversal.
    /// </summary>
    public static IReadOnlyList<Candle> BreakoutThenRun(
        int trendBars = 60, decimal start = 100m, decimal trendStep = 0.8m, decimal trendBand = 0.04m,
        int baseBars = 10, decimal baseBand = 0.012m, decimal breakoutPct = 0.16m,
        int runDays = 8, decimal runStep = 0.06m, int reverseDays = 6, decimal reverseStep = 0.05m,
        long baseVolume = 1_000_000, long breakoutVolume = 3_000_000)
    {
        var list = new List<Candle>();
        var day = 0;
        var price = start;
        for (var i = 0; i < trendBars; i++) { list.Add(Bar(day++, price, trendBand, baseVolume)); price += trendStep; }

        var level = price;
        for (var i = 0; i < baseBars; i++) list.Add(Bar(day++, level, baseBand, baseVolume));

        var bClose = level * (1 + breakoutPct);
        list.Add(new Candle(Start.AddDays(day++), level, bClose * 1.005m, level * 0.995m, bClose, breakoutVolume));

        var p = bClose;
        for (var i = 0; i < runDays; i++) { p *= 1 + runStep; list.Add(Bar(day++, p, 0.02m, baseVolume)); }      // run up
        for (var i = 0; i < reverseDays; i++) { p *= 1 - reverseStep; list.Add(Bar(day++, p, 0.02m, baseVolume)); } // reversal
        return list;
    }

    /// <summary>Same shape as a squeeze breakout but with tiny ranges — fails the high-volatility floor.</summary>
    public static IReadOnlyList<Candle> LowVolBreakout() =>
        HighVolSqueezeBreakout(trendBand: 0.006m, baseBand: 0.003m, breakoutPct: 0.02m);

    /// <summary>A long down-trend — price below its 50-day SMA, so the trend filter blocks longs.</summary>
    public static IReadOnlyList<Candle> Downtrend(int bars = 120, decimal start = 200m, decimal fall = 0.8m, decimal band = 0.04m)
    {
        var list = new List<Candle>(bars);
        var price = start;
        for (var i = 0; i < bars; i++) { list.Add(Bar(i, price, band)); price = Math.Max(1m, price - fall); }
        return list;
    }
}
