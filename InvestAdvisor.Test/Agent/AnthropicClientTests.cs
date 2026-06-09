using System.Net;
using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Anthropic;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class AnthropicClientTests
{
    private static (AnthropicClient client, StubHttpMessageHandler handler) BuildSut(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody, StatusCode = status };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        var opts = Options.Create(new AnthropicOptions
        {
            ApiKey = "test-key",
            Model = "claude-sonnet-4-6",
            MaxTokens = 1024,
        });
        return (new AnthropicClient(http, opts), handler);
    }

    [Fact]
    public async Task Happy_path_extracts_AgentAnalysis_from_tool_use_block()
    {
        var responseBody = """
        {
          "id": "msg_1",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "stop_reason": "tool_use",
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_1",
              "name": "emit_analysis",
              "input": {
                "summary": "AAPL up 2%; allocation drift in BND.",
                "flags": [
                  { "severity": "warn", "ticker": "AAPL", "title": "Strong day", "detail": "Up 2%", "evidence": ["price change"] }
                ],
                "driftAlerts": [
                  { "severity": "action_suggested", "ticker": "BND", "currentPct": 22.5, "targetPct": 30.0, "driftPct": -7.5, "note": "Bond allocation low" }
                ],
                "considerations": [
                  { "topic": "tax", "text": "Consider tax-loss harvesting opportunities." }
                ]
              }
            }
          ],
          "usage": { "input_tokens": 1234, "output_tokens": 567 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.AnalyzeAsync("system prompt", """{"holdings":[]}""");

        result.Analysis.Summary.Should().StartWith("AAPL up 2%");
        result.Analysis.Flags.Should().HaveCount(1);
        result.Analysis.Flags[0].Severity.Should().Be(FlagSeverity.Warn);
        result.Analysis.Flags[0].Ticker.Should().Be("AAPL");
        result.Analysis.Flags[0].Evidence.Should().NotBeNull();
        result.Analysis.DriftAlerts.Should().HaveCount(1);
        result.Analysis.DriftAlerts[0].Severity.Should().Be(DriftSeverity.ActionSuggested);
        result.Analysis.DriftAlerts[0].DriftPct.Should().Be(-7.5m);
        result.Analysis.Considerations.Should().HaveCount(1);
        result.Analysis.Considerations[0].Topic.Should().Be("tax");
        result.InputTokens.Should().Be(1234);
        result.OutputTokens.Should().Be(567);
        result.Model.Should().Be("claude-sonnet-4-6");
        result.ParseFallbackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task Forced_tool_choice_and_emit_analysis_tool_are_in_request()
    {
        var responseBody = MinimalToolUseResponse();
        var (sut, handler) = BuildSut(responseBody);

        await sut.AnalyzeAsync("sys", """{"x":1}""");

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody!.Should().Contain("\"tool_choice\"");
        handler.LastRequestBody!.Should().Contain("\"name\":\"emit_analysis\"");
        handler.LastRequestBody!.Should().Contain("\"model\":\"claude-sonnet-4-6\"");
        handler.LastRequestBody!.Should().Contain("\"system\":\"sys\"");
    }

    [Fact]
    public async Task Missing_tool_use_falls_back_to_first_balanced_json_in_text()
    {
        var fallbackJson = """
        {
          "summary": "Fallback summary.",
          "flags": [],
          "driftAlerts": [],
          "considerations": []
        }
        """;
        var responseBody = $$"""
        {
          "type": "message",
          "model": "claude-sonnet-4-6",
          "content": [
            { "type": "text", "text": "Sorry — here is JSON: {{fallbackJson.Replace("\"", "\\\"").Replace("\n", "\\n")}}" }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 2 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.AnalyzeAsync("sys", "{}");

        result.ParseFallbackUsed.Should().BeTrue();
        result.Analysis.Summary.Should().Be("Fallback summary.");
    }

    [Fact]
    public async Task No_tool_use_and_no_json_throws_AgentParseException()
    {
        var responseBody = """
        {
          "type": "message",
          "model": "claude-sonnet-4-6",
          "content": [ { "type": "text", "text": "I cannot do that." } ],
          "usage": { "input_tokens": 1, "output_tokens": 2 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var act = () => sut.AnalyzeAsync("sys", "{}");

        await act.Should().ThrowAsync<AgentParseException>();
    }

    [Fact]
    public async Task Non_success_status_throws_HttpRequestException()
    {
        var (sut, _) = BuildSut("""{"error":"unauthorized"}""", HttpStatusCode.Unauthorized);

        var act = () => sut.AnalyzeAsync("sys", "{}");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Missing_api_key_throws_immediately()
    {
        var handler = new StubHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") };
        var opts = Options.Create(new AnthropicOptions { ApiKey = "" });
        var sut = new AnthropicClient(http, opts);

        var act = () => sut.AnalyzeAsync("sys", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Anthropic API key not configured*");
    }

    [Theory]
    [InlineData("noise {\"a\": 1} trailing", "{\"a\": 1}")]
    [InlineData("prefix {\"a\": \"{ not real }\"} suffix", "{\"a\": \"{ not real }\"}")]
    [InlineData("nested {\"a\": {\"b\": 1}} ok", "{\"a\": {\"b\": 1}}")]
    [InlineData("no braces here", null)]
    [InlineData("unterminated {\"a\": 1", null)]
    [InlineData("escaped \"{\\\"k\\\":1}\" {\"real\":1}", "{\"real\":1}")]
    public void ExtractFirstJsonObject_handles_strings_and_nesting(string input, string? expected)
    {
        var result = AnthropicClient.ExtractFirstJsonObject(input);
        result.Should().Be(expected);
    }

    private static string MinimalToolUseResponse() => """
        {
          "type": "message",
          "model": "claude-sonnet-4-6",
          "content": [
            { "type": "tool_use", "id": "t1", "name": "emit_analysis",
              "input": { "summary": "s", "flags": [], "driftAlerts": [], "considerations": [] }
            }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 2 }
        }
        """;
}
