using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Momentum;

/// <summary>
/// Indicator helpers specific to the high-volatility breakout engine, layered on top of the shared
/// <see cref="Swing.Indicators"/> math (ATR, RSI, relative volume, …). Like the shared set these are
/// pure, deterministic, oldest-first, and return null when there aren't enough bars — so live scoring
/// and the backtest compute identical values.
/// </summary>
public static class MomentumIndicators
{
    /// <summary>
    /// Tightness of the consolidation <i>base</i> that precedes a breakout: the high-to-low range of
    /// the <paramref name="lookback"/> sessions ending just <b>before</b> the latest bar, as a fraction
    /// of the latest close. A small value means price coiled into a tight range (a "squeeze"); the
    /// breakout bar itself is excluded so its expansion doesn't widen the base. Null if short.
    /// </summary>
    public static decimal? ConsolidationRangePct(IReadOnlyList<Candle> candles, int lookback)
    {
        if (lookback <= 0 || candles.Count < lookback + 1) return null;
        var close = candles[^1].Close;
        if (close <= 0m) return null;

        decimal hi = decimal.MinValue, lo = decimal.MaxValue;
        var start = candles.Count - 1 - lookback; // bars [start .. count-2] — the base, pre-breakout
        for (var i = start; i < candles.Count - 1; i++)
        {
            if (candles[i].High > hi) hi = candles[i].High;
            if (candles[i].Low < lo) lo = candles[i].Low;
        }
        if (lo <= 0m || hi < lo) return null;
        return (hi - lo) / close;
    }

    /// <summary>
    /// Average True Range expressed as a fraction of the latest close — "how big is a normal day,
    /// relative to price". This is the gate that keeps the universe to names volatile enough that a
    /// double-digit move in a 3-day hold is actually reachable. Null if ATR can't be computed.
    /// </summary>
    public static decimal? AtrPercent(IReadOnlyList<Candle> candles, int atrPeriod)
    {
        var atr = Swing.Indicators.Atr(candles, atrPeriod);
        var close = candles.Count > 0 ? candles[^1].Close : 0m;
        return atr is { } a && close > 0m ? a / close : null;
    }
}
