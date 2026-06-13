using FluentAssertions;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Swing;
using Xunit;

namespace InvestAdvisor.Test.Swing;

public class IndicatorsTests
{
    [Fact]
    public void Sma_is_the_mean_of_the_last_period_values()
    {
        Indicators.Sma(new decimal[] { 2, 4, 6 }, 3).Should().Be(4m);
        Indicators.Sma(new decimal[] { 1, 2, 3, 4 }, 2).Should().Be(3.5m);
        Indicators.Sma(new decimal[] { 1, 2 }, 3).Should().BeNull(); // too short
    }

    [Fact]
    public void Ema_of_a_constant_series_is_that_constant()
    {
        Indicators.Ema(SwingTestData.Flat(10, 5m).Select(c => c.Close).ToList(), 3).Should().Be(5m);
    }

    [Fact]
    public void Rsi_is_100_for_a_pure_uptrend_and_0_for_a_pure_downtrend()
    {
        var up = Enumerable.Range(1, 30).Select(i => (decimal)i).ToList();
        var down = Enumerable.Range(1, 30).Select(i => (decimal)(31 - i)).ToList();
        Indicators.Rsi(up, 14).Should().Be(100m);
        Indicators.Rsi(down, 14).Should().Be(0m);
    }

    [Fact]
    public void Rsi_sits_mid_range_for_a_mixed_uptrend()
    {
        var rsi = Indicators.Rsi(SwingTestData.UptrendWithDips(60).Select(c => c.Close).ToList(), 14);
        rsi.Should().NotBeNull();
        rsi!.Value.Should().BeInRange(45m, 75m); // the qualifying band
    }

    [Fact]
    public void Atr_of_constant_range_bars_equals_that_range()
    {
        // Every bar spans 2 around a flat 100 close: H=101, L=99, no gaps → TR=2 each → ATR=2.
        var candles = Enumerable.Range(0, 30)
            .Select(i => new Candle(new DateTime(2026, 1, 1).AddDays(i), 100m, 101m, 99m, 100m, 1000))
            .ToList();
        Indicators.Atr(candles, 14).Should().Be(2m);
    }

    [Fact]
    public void BreakoutStrength_is_positive_when_close_clears_the_prior_high()
    {
        // 21 flat bars at 100, then a close at 110 → breakout of ~10% over the prior 20-day high.
        var closes = Enumerable.Repeat(100m, 21).Append(110m);
        var candles = SwingTestData.FromCloses(closes, bandPct: 0m);
        var b = Indicators.BreakoutStrength(candles, 20);
        b.Should().NotBeNull();
        b!.Value.Should().BeApproximately(0.10m, 0.001m);
    }

    [Fact]
    public void RelativeVolume_compares_latest_to_the_trailing_average()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 20; i++) candles.Add(SwingTestData.Bar(i, 100m, volume: 1_000_000));
        candles.Add(SwingTestData.Bar(20, 100m, volume: 2_000_000)); // 2x the average
        Indicators.RelativeVolume(candles, 20)!.Value.Should().BeApproximately(2m, 0.001m);
    }

    [Fact]
    public void Gap_is_open_minus_prior_close_over_prior_close()
    {
        var candles = new List<Candle>
        {
            new(new DateTime(2026, 1, 1), 100m, 101m, 99m, 100m, 1000),
            new(new DateTime(2026, 1, 2), 105m, 106m, 104m, 105m, 1000), // opened at 105 vs prior 100 close
        };
        Indicators.Gap(candles)!.Value.Should().BeApproximately(0.05m, 0.0001m);
    }

    [Fact]
    public void AverageDollarVolume_multiplies_close_by_volume()
    {
        var candles = SwingTestData.Flat(20, price: 50m, volume: 100_000); // 50 * 100k = 5M each
        Indicators.AverageDollarVolume(candles, 20).Should().Be(5_000_000m);
    }
}
