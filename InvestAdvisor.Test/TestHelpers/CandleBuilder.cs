using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>Synthetic daily bars with known shapes, shared by every strategy test.</summary>
internal static class CandleBuilder
{
    public static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A bar built from a close, with a fixed-fraction high/low band and given volume.</summary>
    public static Candle Bar(int dayIndex, decimal close, decimal bandPct, long volume = 1_000_000) =>
        new(Start.AddDays(dayIndex), close, close * (1 + bandPct), close * (1 - bandPct), close, volume);

    /// <summary>Flat series — every close identical (zero volatility, zero ATR).</summary>
    public static IReadOnlyList<Candle> Flat(int count, decimal price = 100m, long volume = 1_000_000)
    {
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++) list.Add(new Candle(Start.AddDays(i), price, price, price, price, volume));
        return list;
    }

    /// <summary>A series with given explicit closes, fixed band and volume.</summary>
    public static IReadOnlyList<Candle> FromCloses(IEnumerable<decimal> closes, decimal bandPct, long volume = 1_000_000)
    {
        var list = new List<Candle>();
        var i = 0;
        foreach (var c in closes) list.Add(Bar(i++, c, bandPct, volume));
        return list;
    }
}
