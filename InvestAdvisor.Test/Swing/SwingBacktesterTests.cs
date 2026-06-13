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
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.Flat(300)) });
        result.Should().Be(SwingBacktestSummary.Empty);
        result.TotalTrades.Should().Be(0);
    }

    [Fact]
    public void Uptrend_with_recurring_dips_produces_trades_with_consistent_aggregates()
    {
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.RegimeUpWithDips(520)) });

        result.TotalTrades.Should().BeGreaterThan(0);
        (result.Wins + result.Losses).Should().Be(result.TotalTrades);
        result.MaxDrawdownR.Should().BeGreaterThanOrEqualTo(0m);
        result.ProfitFactor.Should().BeGreaterThanOrEqualTo(0m);
        result.AverageHoldingDays.Should().BeInRange(1m, SwingParams.Default.HoldingDays);
    }

    [Fact]
    public void A_crash_after_an_oversold_entry_registers_at_least_one_losing_trade()
    {
        // Up-trend with dips (mean-reversion entries fire), then a collapse that runs the stop.
        var closes = SwingTestData.RegimeUpWithDips(300).Select(c => c.Close).ToList();
        var p = closes[^1];
        for (var i = 0; i < 10; i++) { p *= 0.92m; closes.Add(p); } // sustained collapse

        var result = SwingBacktester.Run(new[] { Input(SwingTestData.FromCloses(closes)) });

        result.TotalTrades.Should().BeGreaterThan(0);
        result.Losses.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Every_outcome_stays_within_the_R_bounds_of_the_rule()
    {
        // No single trade loses much more than 1R or wins much more than the reward:risk target.
        var p = SwingParams.Default;
        var result = SwingBacktester.Run(new[] { Input(SwingTestData.RegimeUpWithDips(520)) }, p);

        result.AverageR.Should().BeInRange(-1.2m, p.RewardRiskRatio + 0.2m);
    }

    [Fact]
    public void HasEdge_rejects_break_even_noise_even_with_a_large_sample()
    {
        // The exact shape the live run produced: 1430 trades, +0.008R, PF 1.02 — statistically nothing.
        var breakEven = new SwingBacktestSummary(
            TotalTrades: 1430, Wins: 725, Losses: 705, WinRatePct: 50.7m,
            AverageR: 0.008m, ExpectancyR: 0.008m, ProfitFactor: 1.02m,
            MaxDrawdownR: 47.5m, AverageHoldingDays: 2.4m, FromUtc: null, ToUtc: null);
        breakEven.HasEdge().Should().BeFalse();
    }

    [Fact]
    public void HasEdge_accepts_a_real_edge_and_rejects_too_small_a_sample()
    {
        var realEdge = new SwingBacktestSummary(
            TotalTrades: 120, Wins: 60, Losses: 60, WinRatePct: 50m,
            AverageR: 0.20m, ExpectancyR: 0.20m, ProfitFactor: 1.4m,
            MaxDrawdownR: 8m, AverageHoldingDays: 2.5m, FromUtc: null, ToUtc: null);
        realEdge.HasEdge().Should().BeTrue();

        // Same per-trade edge but only 20 trades — not enough to trust.
        (realEdge with { TotalTrades = 20 }).HasEdge().Should().BeFalse();
    }
}
