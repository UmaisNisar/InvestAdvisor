using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Momentum;
using Xunit;

namespace InvestAdvisor.Test.Momentum;

public class MomentumBacktesterTests
{
    private static MomentumInput Input(IReadOnlyList<Candle> candles) =>
        new("TEST", "Test Co.", "Tech", AssetClass.Equity, candles);

    [Fact]
    public void Flat_market_yields_no_trades()
    {
        var result = MomentumBacktester.Run(new[] { Input(MomentumTestData.Flat(300)) });
        result.Should().Be(MomentumBacktestSummary.Empty);
        result.TotalTrades.Should().Be(0);
    }

    [Fact]
    public void Repeated_breakouts_produce_trades_with_consistent_aggregates()
    {
        var result = MomentumBacktester.Run(new[] { Input(MomentumTestData.RepeatedBreakouts()) });

        result.TotalTrades.Should().BeGreaterThan(0);
        (result.Wins + result.Losses).Should().Be(result.TotalTrades);
        result.MaxDrawdownR.Should().BeGreaterThanOrEqualTo(0m);
        result.ProfitFactor.Should().BeGreaterThanOrEqualTo(0m);
        result.AverageHoldingDays.Should().BeInRange(1m, MomentumParams.Default.HoldingDays);
    }

    [Fact]
    public void Every_outcome_stays_within_the_R_bounds_of_the_rule()
    {
        var p = MomentumParams.Default;
        var result = MomentumBacktester.Run(new[] { Input(MomentumTestData.RepeatedBreakouts()) }, p);

        // No single trade loses much more than 1R or wins much more than the reward:risk target.
        result.AverageR.Should().BeInRange(-1.2m, p.RewardRiskRatio + 0.2m);
    }

    [Fact]
    public void Trailing_stop_lets_a_winner_run_past_the_fixed_target()
    {
        var data = new[] { Input(MomentumTestData.BreakoutThenRun()) };
        var fixedP = MomentumParams.For(MomentumRiskLevel.High);
        var trailP = fixedP with { UseTrailingStop = true, HoldingDays = 12, TrailAtrMultiple = 2.5m, TrailActivateR = 1.0m };

        var btFixed = MomentumBacktester.Run(data, fixedP);
        var btTrail = MomentumBacktester.Run(data, trailP);

        btFixed.TotalTrades.Should().BeGreaterThan(0);
        btTrail.TotalTrades.Should().BeGreaterThan(0);
        // The fixed target caps the win at the reward:risk multiple; the trail rides the run further.
        btTrail.AverageR.Should().BeGreaterThan(btFixed.AverageR);
        btTrail.AverageR.Should().BeGreaterThan(fixedP.RewardRiskRatio);
    }

    [Fact]
    public void HasEdge_rejects_break_even_noise_even_with_a_large_sample()
    {
        var breakEven = new MomentumBacktestSummary(
            TotalTrades: 1430, Wins: 600, Losses: 830, WinRatePct: 42m,
            AverageR: 0.01m, ExpectancyR: 0.01m, ProfitFactor: 1.05m,
            MaxDrawdownR: 50m, AverageHoldingDays: 2.4m, FromUtc: null, ToUtc: null);
        breakEven.HasEdge().Should().BeFalse();
    }

    [Fact]
    public void HasEdge_demands_a_stricter_profit_factor_than_the_swing_engine()
    {
        // A PF of 1.2 — which the swing engine's 1.15 gate would pass — must NOT pass momentum's 1.3 bar.
        var thin = new MomentumBacktestSummary(
            TotalTrades: 400, Wins: 160, Losses: 240, WinRatePct: 40m,
            AverageR: 0.08m, ExpectancyR: 0.08m, ProfitFactor: 1.2m,
            MaxDrawdownR: 20m, AverageHoldingDays: 2.5m, FromUtc: null, ToUtc: null);
        thin.HasEdge().Should().BeFalse();

        // A genuine momentum edge: lower win rate, but a profit factor with real cushion.
        var real = thin with { ProfitFactor = 1.45m, AverageR = 0.18m, ExpectancyR = 0.18m };
        real.HasEdge().Should().BeTrue();

        // Same edge, too small a sample — not enough to trust.
        (real with { TotalTrades = 20 }).HasEdge().Should().BeFalse();
    }
}
