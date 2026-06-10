using FluentAssertions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class TickerHallucinationValidatorTests
{
    private static AgentAnalysis MakeAnalysis(params Flag[] flags) =>
        new("summary", flags, Array.Empty<DriftAlert>(), Array.Empty<Consideration>(),
            new AgentRunMetrics("m", 0, 0, 0, false), Array.Empty<PositionCall>());

    [Fact]
    public void Known_ticker_is_marked_KnownTicker_true()
    {
        var input = MakeAnalysis(new Flag(FlagSeverity.Info, "AAPL", "t", "d", null));

        var result = TickerHallucinationValidator.Validate(input, new[] { "AAPL", "MSFT" });

        result.Flags[0].KnownTicker.Should().BeTrue();
    }

    [Fact]
    public void Unknown_ticker_is_marked_KnownTicker_false()
    {
        var input = MakeAnalysis(new Flag(FlagSeverity.Warn, "XYZQ", "t", "d", null));

        var result = TickerHallucinationValidator.Validate(input, new[] { "AAPL" });

        result.Flags[0].KnownTicker.Should().BeFalse();
    }

    [Fact]
    public void Null_ticker_is_treated_as_market_wide_and_known()
    {
        var input = MakeAnalysis(new Flag(FlagSeverity.Info, null, "macro", "d", null));

        var result = TickerHallucinationValidator.Validate(input, Array.Empty<string>());

        result.Flags[0].KnownTicker.Should().BeTrue();
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        var input = MakeAnalysis(new Flag(FlagSeverity.Info, "aapl", "t", "d", null));

        var result = TickerHallucinationValidator.Validate(input, new[] { "AAPL" });

        result.Flags[0].KnownTicker.Should().BeTrue();
    }

    [Fact]
    public void Position_calls_are_validated_too()
    {
        var input = new AgentAnalysis("summary", Array.Empty<Flag>(), Array.Empty<DriftAlert>(),
            Array.Empty<Consideration>(), new AgentRunMetrics("m", 0, 0, 0, false),
            new[]
            {
                new PositionCall("AAPL", PositionStance.Hold, PositionConviction.High, "r"),
                new PositionCall("XYZQ", PositionStance.Add, PositionConviction.High, "r"),
            });

        var result = TickerHallucinationValidator.Validate(input, new[] { "AAPL" });

        result.Positions[0].KnownTicker.Should().BeTrue();
        result.Positions[1].KnownTicker.Should().BeFalse(); // a buy call on an invented ticker must be flagged
    }

    [Fact]
    public void Drift_alerts_are_validated_too()
    {
        var input = new AgentAnalysis("summary", Array.Empty<Flag>(),
            new[]
            {
                new DriftAlert(DriftSeverity.Note, "AAPL", 60m, 50m, 10m, "n"),
                new DriftAlert(DriftSeverity.Note, "XYZQ", 60m, 50m, 10m, "n"),
            },
            Array.Empty<Consideration>(), new AgentRunMetrics("m", 0, 0, 0, false),
            Array.Empty<PositionCall>());

        var result = TickerHallucinationValidator.Validate(input, new[] { "AAPL" });

        result.DriftAlerts[0].KnownTicker.Should().BeTrue();
        result.DriftAlerts[1].KnownTicker.Should().BeFalse();
    }
}
