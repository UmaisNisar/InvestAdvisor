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
}
