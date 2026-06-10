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
            { "type": "text", "text": "Sorry — here is JSON: {{fallbackJson.Replace("\r", "").Replace("\"", "\\\"").Replace("\n", "\\n")}}" }
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

    [Fact]
    public async Task Positions_parse_ticker_stance_and_conviction()
    {
        var responseBody = """
        {
          "type": "message",
          "model": "claude-sonnet-4-6",
          "stop_reason": "tool_use",
          "content": [
            { "type": "tool_use", "id": "t1", "name": "emit_analysis",
              "input": {
                "summary": "s", "flags": [], "driftAlerts": [], "considerations": [],
                "positions": [
                  { "ticker": "aapl", "stance": "trim", "conviction": "high",   "reason": "overweight" },
                  { "ticker": "BND",  "stance": "hold", "conviction": "low",    "reason": "fine" },
                  { "ticker": "NVDA", "stance": "add",  "conviction": "medium", "reason": "momentum" }
                ]
              }
            }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 2 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.AnalyzeAsync("system prompt", """{"holdings":[]}""");

        result.Analysis.Positions.Should().HaveCount(3);
        var aapl = result.Analysis.Positions.Single(p => p.Ticker == "AAPL"); // ticker upper-cased
        aapl.Stance.Should().Be(PositionStance.Trim);
        aapl.Conviction.Should().Be(PositionConviction.High);
        result.Analysis.Positions.Single(p => p.Ticker == "BND").Stance.Should().Be(PositionStance.Hold);
        result.Analysis.Positions.Single(p => p.Ticker == "NVDA").Conviction.Should().Be(PositionConviction.Medium);
    }

    [Fact]
    public async Task Missing_positions_array_yields_empty_list_not_null()
    {
        var (sut, _) = BuildSut(MinimalToolUseResponse());
        var result = await sut.AnalyzeAsync("p", "{}");
        result.Analysis.Positions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ScoreSentiment_parses_scores_by_index_from_tool_use()
    {
        var responseBody = """
        {
          "type": "message",
          "model": "claude-haiku-4-5",
          "stop_reason": "tool_use",
          "content": [
            { "type": "tool_use", "id": "t1", "name": "emit_sentiment_scores",
              "input": {
                "scores": [
                  { "index": 0, "score": 0.8, "label": "bullish" },
                  { "index": 1, "score": -0.6, "label": "bearish" },
                  { "index": 2, "score": 0.0, "label": "neutral" }
                ]
              }
            }
          ],
          "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.ScoreSentimentAsync(new[] { "AAPL: up big", "TSLA: recall", "MSFT: filing" });

        result.Scores.Should().HaveCount(3);
        result.Scores.Single(s => s.Index == 0).Score.Should().Be(0.8m);
        result.Scores.Single(s => s.Index == 0).Label.Should().Be("bullish");
        result.Scores.Single(s => s.Index == 1).Score.Should().Be(-0.6m);
        result.Model.Should().Be("claude-haiku-4-5");
        result.ParseFallbackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ScoreSentiment_uses_routine_model_and_forces_tool()
    {
        var (sut, handler) = BuildSut("""
        {
          "type": "message", "model": "claude-haiku-4-5", "content": [
            { "type": "tool_use", "id": "t1", "name": "emit_sentiment_scores", "input": { "scores": [] } }
          ], "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """);

        await sut.ScoreSentimentAsync(new[] { "AAPL: news" });

        handler.LastRequestBody.Should().Contain("claude-haiku-4-5");      // routine model, not primary
        handler.LastRequestBody.Should().Contain("emit_sentiment_scores");
        handler.LastRequestBody.Should().Contain("\"tool_choice\"");
    }

    [Fact]
    public async Task ScoreSentiment_clamps_out_of_range_and_falls_back_to_text_json()
    {
        // No tool_use block — scorer must extract the JSON object from text content.
        var responseBody = """
        {
          "type": "message",
          "model": "claude-haiku-4-5",
          "content": [
            { "type": "text", "text": "Here: { \"scores\": [ { \"index\": 0, \"score\": 5, \"label\": \"bullish\" } ] }" }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.ScoreSentimentAsync(new[] { "AAPL: moon" });

        result.ParseFallbackUsed.Should().BeTrue();
        result.Scores.Single().Score.Should().Be(1m); // clamped from 5 to +1
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
