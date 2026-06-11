using System.Net;
using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.OpenAiCompat;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class OpenAiCompatibleClientTests
{
    private static readonly LlmEndpoint GeminiEndpoint = new(
        "https://generativelanguage.googleapis.com/v1beta/openai/", "test-key", "Gemini");

    private static (OpenAiCompatibleClient client, StubHttpMessageHandler handler) BuildSut(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody, StatusCode = status };
        var http = new HttpClient(handler);
        var opts = Options.Create(new LlmOptions { MaxTokens = 1024 });
        return (new OpenAiCompatibleClient(http, opts), handler);
    }

    private static string ToolCallResponse(string toolName, string argumentsJson) => $$"""
        {
          "id": "chatcmpl-1",
          "model": "gemini-2.5-flash",
          "choices": [
            {
              "finish_reason": "tool_calls",
              "message": {
                "content": null,
                "tool_calls": [
                  { "id": "call_1", "type": "function",
                    "function": { "name": "{{toolName}}", "arguments": {{System.Text.Json.JsonSerializer.Serialize(argumentsJson)}} } }
                ]
              }
            }
          ],
          "usage": { "prompt_tokens": 321, "completion_tokens": 45 }
        }
        """;

    [Fact]
    public async Task AnalyzeAsync_parses_tool_call_arguments_string()
    {
        var args = """{"summary":"All good.","flags":[],"driftAlerts":[],"considerations":[]}""";
        var (sut, handler) = BuildSut(ToolCallResponse("emit_analysis", args));

        var result = await sut.AnalyzeAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        result.Analysis.Summary.Should().Be("All good.");
        result.Model.Should().Be("gemini-2.5-flash");
        result.InputTokens.Should().Be(321);
        result.OutputTokens.Should().Be(45);
        result.ParseFallbackUsed.Should().BeFalse();

        // Request shape: URL joins the base preserving /v1beta/openai/, forced named function.
        handler.LastRequest!.RequestUri!.ToString()
            .Should().Be("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("test-key");
        handler.LastRequestBody.Should().Contain("\"tool_choice\"");
        handler.LastRequestBody.Should().Contain("\"type\":\"function\"");
        handler.LastRequestBody.Should().Contain("\"name\":\"emit_analysis\"");
        handler.LastRequestBody.Should().NotContain("additionalProperties");
    }

    [Fact]
    public async Task Base_url_without_trailing_slash_still_joins_correctly()
    {
        var args = """{"summary":"s","flags":[],"driftAlerts":[],"considerations":[]}""";
        var (sut, handler) = BuildSut(ToolCallResponse("emit_analysis", args));
        var endpoint = new LlmEndpoint("https://api.groq.com/openai/v1", "k", "Custom LLM");

        await sut.AnalyzeAsync(endpoint, "llama-3.3-70b-versatile", "sys", "{}");

        handler.LastRequest!.RequestUri!.ToString()
            .Should().Be("https://api.groq.com/openai/v1/chat/completions");
    }

    [Fact]
    public async Task Blank_api_key_sends_no_authorization_header()
    {
        var args = """{"summary":"s","flags":[],"driftAlerts":[],"considerations":[]}""";
        var (sut, handler) = BuildSut(ToolCallResponse("emit_analysis", args));
        var ollama = new LlmEndpoint("http://localhost:11434/v1/", "", "Custom LLM");

        await sut.AnalyzeAsync(ollama, "llama3.2", "sys", "{}");

        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeStockAsync_maps_fields_and_tokens()
    {
        var args = """
        {"summary":"Sum","thesis":"Th","bullishFactors":["b1"],"bearishFactors":["x1"],
         "keyRisks":["r1"],"conviction":80,"convictionLabel":"high"}
        """;
        var (sut, _) = BuildSut(ToolCallResponse("emit_stock_analysis", args));

        var result = await sut.AnalyzeStockAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        result.Summary.Should().Be("Sum");
        result.BullishFactors.Should().ContainSingle().Which.Should().Be("b1");
        result.Conviction.Should().Be(80);
        result.ConvictionLabel.Should().Be("high");
        result.InputTokens.Should().Be(321);
        result.OutputTokens.Should().Be(45);
    }

    [Fact]
    public async Task RecommendAllocationAsync_parses_picks()
    {
        var args = """
        {"summary":"s","caution":"c",
         "stocks":[{"ticker":"shop.to","reason":"strong"}],"etfs":[],"crypto":[]}
        """;
        var (sut, _) = BuildSut(ToolCallResponse("emit_daily_recommendation", args));

        var result = await sut.RecommendAllocationAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        result.Stocks.Should().ContainSingle().Which.Ticker.Should().Be("SHOP.TO");
    }

    [Fact]
    public async Task ScoreSentimentAsync_parses_scores_and_uses_requested_model()
    {
        var args = """{"scores":[{"index":0,"score":0.8,"label":"bullish"},{"index":1,"score":-0.6,"label":"bearish"}]}""";
        var (sut, handler) = BuildSut(ToolCallResponse("emit_sentiment_scores", args));

        var result = await sut.ScoreSentimentAsync(
            GeminiEndpoint, "gemini-2.5-flash-lite", new[] { "AAPL: up", "TSLA: recall" });

        result.Scores.Should().HaveCount(2);
        result.Scores.Single(s => s.Index == 0).Label.Should().Be("bullish");
        handler.LastRequestBody.Should().Contain("\"model\":\"gemini-2.5-flash-lite\"");
        handler.LastRequestBody.Should().Contain("emit_sentiment_scores");
    }

    [Fact]
    public async Task Missing_tool_call_falls_back_to_json_in_text_content()
    {
        var responseBody = """
        {
          "model": "gemini-2.5-flash",
          "choices": [
            { "finish_reason": "stop",
              "message": { "content": "Here you go: {\"summary\":\"Fallback.\",\"flags\":[],\"driftAlerts\":[],\"considerations\":[]}" } }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 2 }
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.AnalyzeAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        result.ParseFallbackUsed.Should().BeTrue();
        result.Analysis.Summary.Should().Be("Fallback.");
    }

    [Fact]
    public async Task No_tool_call_and_no_json_throws_AgentParseException()
    {
        var responseBody = """
        {
          "model": "gemini-2.5-flash",
          "choices": [ { "message": { "content": "I cannot do that." } } ]
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var act = () => sut.AnalyzeAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        await act.Should().ThrowAsync<AgentParseException>();
    }

    [Fact]
    public async Task Missing_usage_defaults_token_counts_to_zero()
    {
        var responseBody = """
        {
          "model": "llama3.2",
          "choices": [
            { "message": { "content": null, "tool_calls": [
                { "id": "c1", "type": "function",
                  "function": { "name": "emit_analysis",
                                "arguments": "{\"summary\":\"s\",\"flags\":[],\"driftAlerts\":[],\"considerations\":[]}" } } ] } }
          ]
        }
        """;
        var (sut, _) = BuildSut(responseBody);

        var result = await sut.AnalyzeAsync(GeminiEndpoint, "llama3.2", "sys", "{}");

        result.InputTokens.Should().Be(0);
        result.OutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task Non_success_status_throws_HttpRequestException()
    {
        var (sut, _) = BuildSut("""{"error":{"message":"quota"}}""", HttpStatusCode.TooManyRequests);

        var act = () => sut.AnalyzeAsync(GeminiEndpoint, "gemini-2.5-flash", "sys", "{}");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*429*");
    }
}
