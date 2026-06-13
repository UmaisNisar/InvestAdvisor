using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Test.Swing;

/// <summary>Builders for synthetic candle series with known shapes, used across the swing tests.</summary>
internal static class SwingTestData
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A bar built from a close, with a fixed-fraction high/low band and given volume.</summary>
    public static Candle Bar(int dayIndex, decimal close, decimal bandPct = 0.01m, long volume = 1_000_000) =>
        new(Start.AddDays(dayIndex), close, close * (1 + bandPct), close * (1 - bandPct), close, volume);

    /// <summary>Flat series — every close identical (zero volatility, zero ATR).</summary>
    public static IReadOnlyList<Candle> Flat(int count, decimal price = 100m, long volume = 1_000_000)
    {
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++) list.Add(new Candle(Start.AddDays(i), price, price, price, price, volume));
        return list;
    }

    /// <summary>
    /// Steady up-trend with periodic one-day dips (repeating +1,+1,-1), which keeps RSI in the mid
    /// range (~60–70) and the fast EMA above the slow — the shape a long swing signal qualifies on.
    /// </summary>
    public static IReadOnlyList<Candle> UptrendWithDips(int count, decimal start = 100m, long volume = 2_000_000)
    {
        var list = new List<Candle>(count);
        var price = start;
        for (var i = 0; i < count; i++)
        {
            list.Add(Bar(i, price, volume: volume));
            price += i % 3 == 2 ? -1m : +1m;
            if (price < 1m) price = 1m;
        }
        return list;
    }

    /// <summary>A series with given explicit closes, fixed band and volume.</summary>
    public static IReadOnlyList<Candle> FromCloses(IEnumerable<decimal> closes, decimal bandPct = 0.01m, long volume = 1_000_000)
    {
        var list = new List<Candle>();
        var i = 0;
        foreach (var c in closes) list.Add(Bar(i++, c, bandPct, volume));
        return list;
    }

    /// <summary>
    /// A long up-trend (well above its 200-day SMA) that ends in a sharp multi-day sell-off — the
    /// canonical mean-reversion setup: confirmed up-trend, short-term oversold (RSI(3) ≈ 0).
    /// </summary>
    public static IReadOnlyList<Candle> RegimeUpThenDip(
        int bars = 260, decimal start = 100m, decimal rise = 0.4m, int dipDays = 4, decimal dipStep = 2.5m, long volume = 2_000_000)
    {
        var closes = new List<decimal>(bars);
        var price = start;
        for (var i = 0; i < bars - dipDays; i++) { closes.Add(price); price += rise; }
        for (var i = 0; i < dipDays; i++) { price -= dipStep; closes.Add(price); }
        return FromCloses(closes, volume: volume);
    }

    /// <summary>A long, pure up-trend with no recent dip — extended above its MAs and NOT oversold.</summary>
    public static IReadOnlyList<Candle> RegimeUpNoDip(int bars = 260, decimal start = 100m, decimal rise = 0.7m, long volume = 2_000_000)
    {
        var closes = new List<decimal>(bars);
        var price = start;
        for (var i = 0; i < bars; i++) { closes.Add(price); price += rise; }
        return FromCloses(closes, volume: volume);
    }

    /// <summary>A long down-trend — price below its 200-day SMA, so the regime filter blocks longs.</summary>
    public static IReadOnlyList<Candle> RegimeDown(int bars = 260, decimal start = 200m, decimal fall = 0.4m, long volume = 2_000_000)
    {
        var closes = new List<decimal>(bars);
        var price = start;
        for (var i = 0; i < bars; i++) { closes.Add(price); price = Math.Max(1m, price - fall); }
        return FromCloses(closes, volume: volume);
    }

    /// <summary>
    /// A long up-trend with recurring short, sharp dips (8 up days, then 3 down days). Stays above its
    /// 200-day SMA while repeatedly hitting short-term oversold — produces many mean-reversion entries
    /// for the backtest.
    /// </summary>
    public static IReadOnlyList<Candle> RegimeUpWithDips(
        int bars = 520, decimal start = 100m, int upRun = 8, decimal upStep = 1.2m, int downRun = 3, decimal downStep = 2m, long volume = 2_000_000)
    {
        var closes = new List<decimal>(bars);
        var price = start;
        var up = true;
        var run = 0;
        while (closes.Count < bars)
        {
            closes.Add(price);
            price += up ? upStep : -downStep;
            if (price < 5m) price = 5m;
            if (++run >= (up ? upRun : downRun)) { up = !up; run = 0; }
        }
        return FromCloses(closes, volume: volume);
    }
}
