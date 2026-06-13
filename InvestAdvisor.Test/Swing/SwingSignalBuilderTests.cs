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
    public void Build_returns_null_without_a_full_regime_window()
    {
        // Fewer than 200 bars → can't compute the regime SMA → no plan.
        SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpThenDip(bars: 120)), SwingParams.Default).Should().BeNull();
    }

    [Fact]
    public void Build_returns_null_when_there_is_no_volatility_to_size_a_stop()
    {
        SwingSignalBuilder.Build(Input(SwingTestData.Flat(260)), SwingParams.Default).Should().BeNull();
    }

    [Fact]
    public void Build_produces_a_stop_below_and_a_target_above_the_entry()
    {
        var built = SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpThenDip()), SwingParams.Default);
        built.Should().NotBeNull();
        var setup = built!.Value.Setup;

        setup.StopLoss.Should().BeLessThan(setup.EntryReference);
        setup.Target.Should().BeGreaterThan(setup.EntryReference);
        setup.EntryLow.Should().BeLessThan(setup.EntryHigh);
    }

    [Fact]
    public void Stop_and_target_track_the_ATR_multiples()
    {
        var p = SwingParams.Default with { AtrStopMultiple = 2.5m, TargetAtrMultiple = 1.5m };
        var setup = SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpThenDip()), p)!.Value.Setup;

        var risk = setup.EntryReference - setup.StopLoss;
        var reward = setup.Target - setup.EntryReference;
        // reward:risk == target multiple : stop multiple == 1.5 : 2.5 == 0.6
        (reward / risk).Should().BeApproximately(0.6m, 0.05m);
    }

    [Fact]
    public void Position_size_scales_inversely_with_stop_distance_and_caps_at_the_max()
    {
        var p = SwingParams.Default with { RiskPerTradePct = 1m, MaxPositionPct = 25m };
        var setup = SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpThenDip()), p)!.Value.Setup;

        var expected = Math.Min(25m, 1m / setup.StopDistancePct * 100m);
        setup.PositionSizePct.Should().BeApproximately(expected, 0.05m);
        setup.PositionSizePct.Should().BeLessThanOrEqualTo(25m);
    }

    [Fact]
    public void Qualifies_only_for_an_oversold_pullback_inside_an_uptrend()
    {
        var p = SwingParams.Default;

        // Up-trend + sharp recent dip → above the 200-day SMA AND oversold → qualifies.
        var dip = SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpThenDip()), p)!.Value.Features;
        dip.AboveRegime.Should().BeTrue();
        SwingSignalBuilder.Qualifies(dip, p).Should().BeTrue();

        // Up-trend but no pullback (not oversold) → must NOT qualify (we don't chase strength).
        var noDip = SwingSignalBuilder.Build(Input(SwingTestData.RegimeUpNoDip()), p)!.Value.Features;
        SwingSignalBuilder.Qualifies(noDip, p).Should().BeFalse();

        // Down-trend (below the 200-day SMA) → regime filter blocks it even if oversold.
        var down = SwingSignalBuilder.Build(Input(SwingTestData.RegimeDown()), p)!.Value.Features;
        down.AboveRegime.Should().BeFalse();
        SwingSignalBuilder.Qualifies(down, p).Should().BeFalse();
    }
}
