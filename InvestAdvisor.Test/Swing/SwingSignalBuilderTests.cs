using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Swing;
using Xunit;

namespace InvestAdvisor.Test.Swing;

public class SwingSignalBuilderTests
{
    private static SwingInput Input(IReadOnlyList<Core.Models.Candle> candles) =>
        new("TEST", "Test Co.", "Tech", AssetClass.Equity, candles);

    [Fact]
    public void Build_returns_null_when_there_is_no_volatility_to_size_a_stop()
    {
        // Flat series → ATR 0 → no risk-bounded plan.
        SwingSignalBuilder.Build(Input(SwingTestData.Flat(40)), SwingParams.Default).Should().BeNull();
    }

    [Fact]
    public void Build_produces_a_stop_below_and_a_target_above_the_entry()
    {
        var built = SwingSignalBuilder.Build(Input(SwingTestData.UptrendWithDips(60)), SwingParams.Default);
        built.Should().NotBeNull();
        var setup = built!.Value.Setup;

        setup.StopLoss.Should().BeLessThan(setup.EntryReference);
        setup.Target.Should().BeGreaterThan(setup.EntryReference);
        setup.EntryLow.Should().BeLessThan(setup.EntryHigh);
    }

    [Fact]
    public void Target_distance_is_reward_risk_times_the_stop_distance()
    {
        var p = SwingParams.Default with { RewardRiskRatio = 2m };
        var setup = SwingSignalBuilder.Build(Input(SwingTestData.UptrendWithDips(60)), p)!.Value.Setup;

        var risk = setup.EntryReference - setup.StopLoss;
        var reward = setup.Target - setup.EntryReference;
        (reward / risk).Should().BeApproximately(2m, 0.05m);
    }

    [Fact]
    public void Position_size_scales_inversely_with_stop_distance_and_caps_at_the_max()
    {
        var p = SwingParams.Default with { RiskPerTradePct = 1m, MaxPositionPct = 25m };
        var built = SwingSignalBuilder.Build(Input(SwingTestData.UptrendWithDips(60)), p);
        var setup = built!.Value.Setup;

        // size% == riskPerTrade% / stopDistance% * 100, clamped to the max.
        var expected = Math.Min(25m, 1m / setup.StopDistancePct * 100m);
        setup.PositionSizePct.Should().BeApproximately(expected, 0.05m);
        setup.PositionSizePct.Should().BeLessThanOrEqualTo(25m);
    }

    [Fact]
    public void Qualifies_requires_an_uptrend_and_a_non_extreme_rsi()
    {
        var up = SwingSignalBuilder.Build(Input(SwingTestData.UptrendWithDips(60)), SwingParams.Default)!.Value.Features;
        SwingSignalBuilder.Qualifies(up).Should().BeTrue();

        // A pure parabolic up-move pins RSI at 100 (overbought) → must NOT qualify.
        var parabolic = SwingTestData.FromCloses(Enumerable.Range(1, 40).Select(i => (decimal)i * 2));
        var hot = SwingSignalBuilder.Build(Input(parabolic), SwingParams.Default)!.Value.Features;
        SwingSignalBuilder.Qualifies(hot).Should().BeFalse();
    }
}
