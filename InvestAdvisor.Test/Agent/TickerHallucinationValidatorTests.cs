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
}
