using FluentAssertions;
using InvestAdvisor.Core.Swing;
using Xunit;

namespace InvestAdvisor.Test.Swing;

public class SwingRiskLevelTests
{
    [Fact]
    public void Presets_loosen_and_grow_monotonically_with_risk()
    {
        var low = SwingParams.For(SwingRiskLevel.Low);
        var med = SwingParams.For(SwingRiskLevel.Medium);
        var high = SwingParams.For(SwingRiskLevel.High);

        // Looser oversold trigger as risk rises (more names qualify).
        low.OversoldEntry.Should().BeLessThan(med.OversoldEntry);
        med.OversoldEntry.Should().BeLessThan(high.OversoldEntry);

        // Larger position sizing and more names surfaced as risk rises.
        low.RiskPerTradePct.Should().BeLessThan(high.RiskPerTradePct);
        low.SetupCount.Should().BeLessThan(high.SetupCount);

        // Low is the strictest: only the deep-oversold setup, no gentler MA-bounce.
        low.EnableMaBounce.Should().BeFalse();
        high.EnableMaBounce.Should().BeTrue();
    }

    [Fact]
    public void MaBounce_qualifies_a_shallow_pullback_only_when_enabled()
    {
        // Up-trend, pulled back to ~2% above the 50-day MA, RSI 40 — soft but not deeply oversold.
        var f = new SwingFeatures(
            Close: 105m, RegimeSma: 100m, TrendSma: 103m, Rsi: 40m,
            PullbackPct: 0.02m, RelativeVolume: 1m, Atr: 2m, AverageDollarVolume: 50_000_000m);

        f.AboveRegime.Should().BeTrue();

        // Medium/High enable the MA-bounce setup → qualifies.
        SwingSignalBuilder.Qualifies(f, SwingParams.For(SwingRiskLevel.Medium)).Should().BeTrue();
        SwingSignalBuilder.IsMaBounce(f, SwingParams.For(SwingRiskLevel.High)).Should().BeTrue();

        // Low only takes deep oversold (RSI ≤ 15); 40 isn't, and MA-bounce is off → does not qualify.
        SwingSignalBuilder.Qualifies(f, SwingParams.For(SwingRiskLevel.Low)).Should().BeFalse();
    }

    [Fact]
    public void Higher_risk_qualifies_at_least_as_many_names_as_lower_risk()
    {
        // A name only mildly oversold (RSI between Low's 15 and High's 35) should qualify at High but not Low.
        var mild = new SwingFeatures(
            Close: 98m, RegimeSma: 90m, TrendSma: 95m, Rsi: 30m,
            PullbackPct: 0.03m, RelativeVolume: 1.4m, Atr: 2m, AverageDollarVolume: 50_000_000m);

        SwingSignalBuilder.Qualifies(mild, SwingParams.For(SwingRiskLevel.Low)).Should().BeFalse();
        SwingSignalBuilder.Qualifies(mild, SwingParams.For(SwingRiskLevel.High)).Should().BeTrue();
    }
}
