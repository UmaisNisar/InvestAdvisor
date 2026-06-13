using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>
/// Pure technical-indicator math over a series of daily <see cref="Candle"/> bars. No I/O — every
/// method is a deterministic function of its inputs, so the swing scorer and the backtest harness
/// share the exact same calculations and the lot is trivially unit-testable.
///
/// Candle lists are expected oldest-first (as <see cref="PriceHistory"/> delivers them). Methods
/// return null when there aren't enough bars to compute the value, rather than throwing, so callers
/// can treat "not enough history" the same as "no data".
/// </summary>
public static class Indicators
{
    /// <summary>Simple moving average of the last <paramref name="period"/> values.</summary>
    public static decimal? Sma(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0 || values.Count < period) return null;
        decimal sum = 0m;
        for (var i = values.Count - period; i < values.Count; i++) sum += values[i];
        return sum / period;
    }

    /// <summary>
    /// Exponential moving average of the whole series, returning the final (most recent) value.
    /// Seeded with the SMA of the first <paramref name="period"/> values, then smoothed with
    /// k = 2/(period+1) — the standard EMA recurrence.
    /// </summary>
    public static decimal? Ema(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0 || values.Count < period) return null;
        decimal k = 2m / (period + 1);
        // Seed with the SMA of the first `period` values.
        decimal ema = 0m;
        for (var i = 0; i < period; i++) ema += values[i];
        ema /= period;
        for (var i = period; i < values.Count; i++) ema = values[i] * k + ema * (1 - k);
        return ema;
    }

    /// <summary>
    /// Wilder's RSI over the closing series, 0–100. Needs <paramref name="period"/>+1 closes.
    /// Returns 100 when there are no losses in the window (pure up-move).
    /// </summary>
    public static decimal? Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        if (period <= 0 || closes.Count < period + 1) return null;

        // Seed average gain/loss over the first `period` deltas, then Wilder-smooth the rest.
        decimal gain = 0m, loss = 0m;
        for (var i = 1; i <= period; i++)
        {
            var delta = closes[i] - closes[i - 1];
            if (delta >= 0m) gain += delta; else loss -= delta;
        }
        decimal avgGain = gain / period, avgLoss = loss / period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var delta = closes[i] - closes[i - 1];
            var up = delta > 0m ? delta : 0m;
            var down = delta < 0m ? -delta : 0m;
            avgGain = (avgGain * (period - 1) + up) / period;
            avgLoss = (avgLoss * (period - 1) + down) / period;
        }

        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }

    /// <summary>
    /// Wilder's Average True Range over <paramref name="period"/> bars — a volatility measure in
    /// price units, used to size stop distance. Needs <paramref name="period"/>+1 candles.
    /// </summary>
    public static decimal? Atr(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (period <= 0 || candles.Count < period + 1) return null;

        decimal sum = 0m;
        for (var i = 1; i <= period; i++) sum += TrueRange(candles[i], candles[i - 1]);
        decimal atr = sum / period;

        for (var i = period + 1; i < candles.Count; i++)
            atr = (atr * (period - 1) + TrueRange(candles[i], candles[i - 1])) / period;

        return atr;
    }

    private static decimal TrueRange(Candle cur, Candle prev)
    {
        var hl = cur.High - cur.Low;
        var hc = Math.Abs(cur.High - prev.Close);
        var lc = Math.Abs(cur.Low - prev.Close);
        return Math.Max(hl, Math.Max(hc, lc));
    }

    /// <summary>
    /// How far the latest close sits relative to the highest high of the prior
    /// <paramref name="lookback"/> sessions (the bars *before* the latest), as a fraction:
    /// &gt;0 means a fresh breakout above the range, &lt;0 means still inside it. Null if short.
    /// </summary>
    public static decimal? BreakoutStrength(IReadOnlyList<Candle> candles, int lookback = 20)
    {
        if (lookback <= 0 || candles.Count < lookback + 1) return null;
        decimal priorHigh = 0m;
        var start = candles.Count - 1 - lookback;
        for (var i = start; i < candles.Count - 1; i++)
            if (candles[i].High > priorHigh) priorHigh = candles[i].High;
        if (priorHigh <= 0m) return null;
        return (candles[^1].Close - priorHigh) / priorHigh;
    }

    /// <summary>
    /// Latest session volume divided by the average volume of the prior <paramref name="lookback"/>
    /// sessions. 1.0 = average; &gt;1.5 is a notable spike. Null if short or no prior volume.
    /// </summary>
    public static decimal? RelativeVolume(IReadOnlyList<Candle> candles, int lookback = 20)
    {
        if (lookback <= 0 || candles.Count < lookback + 1) return null;
        long sum = 0L;
        var start = candles.Count - 1 - lookback;
        for (var i = start; i < candles.Count - 1; i++) sum += candles[i].Volume;
        if (sum <= 0L) return null;
        var avg = (decimal)sum / lookback;
        return avg == 0m ? null : candles[^1].Volume / avg;
    }

    /// <summary>Overnight gap: (latest open − prior close) / prior close, as a fraction.</summary>
    public static decimal? Gap(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < 2) return null;
        var prevClose = candles[^2].Close;
        return prevClose == 0m ? null : (candles[^1].Open - prevClose) / prevClose;
    }

    /// <summary>Simple price return over the last <paramref name="sessions"/> bars, as a fraction.</summary>
    public static decimal? Return(IReadOnlyList<Candle> candles, int sessions)
    {
        if (sessions <= 0 || candles.Count < sessions + 1) return null;
        var past = candles[^(sessions + 1)].Close;
        return past == 0m ? null : (candles[^1].Close - past) / past;
    }

    /// <summary>
    /// Trailing average dollar volume (close × volume) over the last <paramref name="sessions"/>
    /// bars — the liquidity gate's input. Null when there isn't enough history.
    /// </summary>
    public static decimal? AverageDollarVolume(IReadOnlyList<Candle> candles, int sessions = 20)
    {
        if (sessions <= 0 || candles.Count < sessions) return null;
        decimal sum = 0m;
        for (var i = candles.Count - sessions; i < candles.Count; i++)
            sum += candles[i].Close * candles[i].Volume;
        return sum / sessions;
    }

    /// <summary>Convenience: extract the closing series oldest-first.</summary>
    public static IReadOnlyList<decimal> Closes(IReadOnlyList<Candle> candles)
    {
        var arr = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++) arr[i] = candles[i].Close;
        return arr;
    }
}
