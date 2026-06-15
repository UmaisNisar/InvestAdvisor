using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Momentum;
using Xunit;

namespace InvestAdvisor.Test.Momentum;

public class MomentumSignalBuilderTests
{
    private static MomentumInput Input(IReadOnlyList<Candle> candles) =>
        new("TEST", "Test Co.", "Tech", AssetClass.Equity, candles);

    [Fact]
    public void Build_returns_null_without_enough_history()
    {
        SmallInput(20).Should().BeNull();

        static (MomentumFeatures, MomentumSetup)? SmallInput(int bars) =>
            MomentumSignalBuilder.Build(
                new MomentumInput("T", "T", "S", AssetClass.Equity, MomentumTestData.Flat(bars)),
                MomentumParams.Default);
    }

    [Fact]
    public void Build_returns_null_when_there_is_no_volatility_to_size_a_stop()
    {
        MomentumSignalBuilder.Build(Input(MomentumTestData.Flat(120)), MomentumParams.Default).Should().BeNull();
    }

    [Fact]
    public void Build_produces_a_stop_below_and_a_target_above_the_entry()
    {
        var built = MomentumSignalBuilder.Build(Input(MomentumTestData.HighVolSqueezeBreakout()), MomentumParams.Default);
        built.Should().NotBeNull();
        var setup = built!.Value.Setup;

        setup.StopLoss.Should().BeLessThan(setup.EntryReference);
        setup.Target.Should().BeGreaterThan(setup.EntryReference);
        setup.EntryLow.Should().BeLessThan(setup.EntryHigh);
    }

    [Fact]
    public void Stop_and_target_track_the_ATR_multiples_so_reward_beats_risk()
    {
        var p = MomentumParams.Default with { StopAtrMultiple = 1.25m, TargetAtrMultiple = 2.5m };
        var setup = MomentumSignalBuilder.Build(Input(MomentumTestData.HighVolSqueezeBreakout()), p)!.Value.Setup;

        var risk = setup.EntryReference - setup.StopLoss;
        var reward = setup.Target - setup.EntryReference;
        // reward:risk == target multiple : stop multiple == 2.5 : 1.25 == 2.0 (asymmetric, unlike swing)
        (reward / risk).Should().BeApproximately(2.0m, 0.05m);
        setup.RewardRiskRatio.Should().BeApproximately(2.0m, 0.05m);
    }

    [Fact]
    public void Position_size_scales_inversely_with_stop_distance_and_caps_at_the_max()
    {
        var p = MomentumParams.Default with { RiskPerTradePct = 1.5m, MaxPositionPct = 30m };
        var setup = MomentumSignalBuilder.Build(Input(MomentumTestData.HighVolSqueezeBreakout()), p)!.Value.Setup;

        var expected = Math.Min(30m, 1.5m / setup.StopDistancePct * 100m);
        setup.PositionSizePct.Should().BeApproximately(expected, 0.05m);
        setup.PositionSizePct.Should().BeLessThanOrEqualTo(30m);
    }

    [Fact]
    public void Qualifies_for_a_high_vol_squeeze_breakout_and_tags_it()
    {
        var p = MomentumParams.Default;
        var built = MomentumSignalBuilder.Build(Input(MomentumTestData.HighVolSqueezeBreakout()), p)!.Value;

        built.Features.AboveTrend.Should().BeTrue();
        built.Features.IsHighVolatility(p).Should().BeTrue();
        MomentumSignalBuilder.Qualifies(built.Features, p).Should().BeTrue();
        built.Setup.Kind.Should().Be(MomentumSetupKind.SqueezeBreakout);
    }

    [Fact]
    public void Does_not_qualify_a_low_volatility_breakout()
    {
        var p = MomentumParams.Default;
        var built = MomentumSignalBuilder.Build(Input(MomentumTestData.LowVolBreakout()), p)!.Value;

        built.Features.IsHighVolatility(p).Should().BeFalse();
        MomentumSignalBuilder.Qualifies(built.Features, p).Should().BeFalse();
    }

    [Fact]
    public void Does_not_qualify_below_the_intermediate_trend()
    {
        var p = MomentumParams.Default;
        var built = MomentumSignalBuilder.Build(Input(MomentumTestData.Downtrend()), p)!.Value;

        built.Features.AboveTrend.Should().BeFalse();
        MomentumSignalBuilder.Qualifies(built.Features, p).Should().BeFalse();
    }

    [Fact]
    public void Squeeze_base_tightness_is_judged_in_ATR_units_not_absolute_percent()
    {
        var p = MomentumParams.For(MomentumRiskLevel.High); // MaxBaseRangeAtr 4.5, MinAtrPercent 0.04, MinRelVol 1.3

        // A high-volatility name (ATR 6% of price) whose base spans 18% of price — wide in absolute
        // terms, but only 3×ATR, i.e. genuinely coiled *for this stock*. Must qualify.
        var tight = new MomentumFeatures(
            Close: 100m, TrendSma: 90m, Atr: 6m, AtrPercent: 0.06m,
            BreakoutStrength: 0.02m, BaseRangePct: 0.18m, RelativeVolume: 1.5m,
            Rsi: 60m, MomentumReturn: 0.10m, AverageDollarVolume: 50_000_000m);
        tight.BaseRangeAtr.Should().BeApproximately(3.0m, 0.01m);
        MomentumSignalBuilder.IsSqueezeBreakout(tight, p).Should().BeTrue();
        MomentumSignalBuilder.Qualifies(tight, p).Should().BeTrue();

        // A 30%-of-price base on a 5%-ATR name = 6×ATR — loose for its own volatility. Must NOT qualify
        // on the squeeze trigger even though it's still high-vol and breaking out.
        var loose = tight with { Atr = 5m, AtrPercent = 0.05m, BaseRangePct = 0.30m, MomentumReturn = 0m };
        loose.BaseRangeAtr.Should().BeApproximately(6.0m, 0.01m);
        MomentumSignalBuilder.IsSqueezeBreakout(loose, p).Should().BeFalse();
    }

    [Fact]
    public void High_risk_preset_targets_roughly_a_double_digit_move()
    {
        var p = MomentumParams.For(MomentumRiskLevel.High);
        var setup = MomentumSignalBuilder.Build(Input(MomentumTestData.HighVolSqueezeBreakout()), p)!.Value.Setup;

        // The whole point of the dial: on a volatile name the High preset projects a ~10%+ target.
        setup.TargetGainPct.Should().BeGreaterThan(9m);
    }
}
