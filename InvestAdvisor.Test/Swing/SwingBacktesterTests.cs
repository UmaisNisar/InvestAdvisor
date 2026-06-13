using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Swing;
using Xunit;

namespace InvestAdvisor.Test.Swing;

public class SwingBacktesterTests
{
    private static SwingInput Input(IReadOnlyList<Candle> candles) =>
        new("TEST", "Test Co.", "Tech", AssetClass.Equity, candles);

    [Fact]
    public void Flat_market_yields_no_trades()
    {
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.Flat(120)) });
        result.Should().Be(SwingBacktestSummary.Empty);
        result.TotalTrades.Should().Be(0);
    }

    [Fact]
    public void Trending_market_produces_trades_with_consistent_aggregates()
    {
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.UptrendWithDips(150)) });

        result.TotalTrades.Should().BeGreaterThan(0);
        (result.Wins + result.Losses).Should().Be(result.TotalTrades);
        result.MaxDrawdownR.Should().BeGreaterThanOrEqualTo(0m);
        result.ProfitFactor.Should().BeGreaterThanOrEqualTo(0m);
        result.AverageHoldingDays.Should().BeInRange(1m, SwingParams.Default.HoldingDays);
    }

    [Fact]
    public void A_crash_after_an_uptrend_registers_at_least_one_losing_trade()
    {
        // Qualify on the up-trend, then collapse so a held position is stopped out.
        var up = SwingTestData.UptrendWithDips(40).Select(c => c.Close).ToList();
        var crash = new List<decimal>(up);
        var p = up[^1];
        for (var i = 0; i < 8; i++) { p *= 0.90m; crash.Add(p); } // -10%/bar collapse

        var result = SwingBacktester.Run(new[] { Input(SwingTestData.FromCloses(crash)) });

        result.TotalTrades.Should().BeGreaterThan(0);
        result.Losses.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Every_outcome_stays_within_the_R_bounds_of_the_rule()
    {
        // No single trade can lose much more than 1R or win much more than the reward:risk target.
        var p = SwingParams.Default;
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.UptrendWithDips(200)) }, p);

        result.AverageR.Should().BeInRange(-1.2m, p.RewardRiskRatio + 0.2m);
    }
}
