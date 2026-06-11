using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers;
using InvestAdvisor.Data.Providers.Anthropic;
using InvestAdvisor.Data.Providers.OpenAiCompat;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class LlmClientRouterTests
{
    private const string MinimalOpenAiResponse = """
        {
          "model": "m",
          "choices": [
            { "message": { "content": null, "tool_calls": [
                { "id": "c1", "type": "function",
                  "function": { "name": "emit_analysis",
                                "arguments": "{\"summary\":\"s\",\"flags\":[],\"driftAlerts\":[],\"considerations\":[]}" } } ] } }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1 }
        }
        """;

    private const string MinimalAnthropicResponse = """
        {
          "type": "message",
          "model": "claude-sonnet-4-6",
          "content": [
            { "type": "tool_use", "id": "t1", "name": "emit_analysis",
              "input": { "summary": "s", "flags": [], "driftAlerts": [], "considerations": [] } }
          ],
          "usage": { "input_tokens": 1, "output_tokens": 2 }
        }
        """;

    private static (LlmClientRouter router,
                    StubHttpMessageHandler anthropicHandler,
                    StubHttpMessageHandler openAiHandler)
        BuildSut(RuntimeSettings settings, LlmOptions? llmOptions = null)
    {
        var anthropicHandler = new StubHttpMessageHandler { ResponseBody = MinimalAnthropicResponse };
        var anthropic = new AnthropicClient(
            new HttpClient(anthropicHandler) { BaseAddress = new Uri("https://api.anthropic.com/") },
            Options.Create(new AnthropicOptions { ApiKey = "ant-key", Model = "claude-sonnet-4-6" }));

        var llmOpts = llmOptions ?? new LlmOptions { GeminiApiKey = "gem-key" };
        var openAiHandler = new StubHttpMessageHandler { ResponseBody = MinimalOpenAiResponse };
        var openAi = new OpenAiCompatibleClient(new HttpClient(openAiHandler), Options.Create(llmOpts));

        var store = Substitute.For<IRuntimeSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>())
             .Returns(new ValueTask<RuntimeSettings>(settings));

        return (new LlmClientRouter(anthropic, openAi, store, Options.Create(llmOpts)),
                anthropicHandler, openAiHandler);
    }

    [Fact]
    public async Task Gemini_provider_routes_to_openai_client_with_gemini_endpoint_and_settings_model()
    {
        var (router, anthropicHandler, openAiHandler) = BuildSut(new RuntimeSettings
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-2.5-flash",
        });

        await router.AnalyzeAsync("sys", "{}");

        anthropicHandler.CallCount.Should().Be(0);
        openAiHandler.CallCount.Should().Be(1);
        openAiHandler.LastRequest!.RequestUri!.ToString()
            .Should().StartWith("https://generativelanguage.googleapis.com/v1beta/openai/");
        openAiHandler.LastRequestBody.Should().Contain("\"model\":\"gemini-2.5-flash\"");
    }

    [Fact]
    public async Task Anthropic_provider_routes_to_anthropic_client()
    {
        var (router, anthropicHandler, openAiHandler) = BuildSut(new RuntimeSettings
        {
            LlmProvider = "anthropic",
            LlmModel = "claude-sonnet-4-6",
        });

        await router.AnalyzeAsync("sys", "{}");

        openAiHandler.CallCount.Should().Be(0);
        anthropicHandler.CallCount.Should().Be(1);
        anthropicHandler.LastRequestBody.Should().Contain("\"model\":\"claude-sonnet-4-6\"");
    }

    [Fact]
    public async Task Custom_provider_uses_runtime_settings_base_url()
    {
        var (router, _, openAiHandler) = BuildSut(new RuntimeSettings
        {
            LlmProvider = "custom",
            LlmModel = "llama-3.3-70b-versatile",
            LlmCustomBaseUrl = "https://api.groq.com/openai/v1/",
        });

        await router.AnalyzeAsync("sys", "{}");

        openAiHandler.LastRequest!.RequestUri!.ToString()
            .Should().Be("https://api.groq.com/openai/v1/chat/completions");
    }

    [Fact]
    public async Task Caller_model_override_wins_over_settings_model()
    {
        var (router, _, openAiHandler) = BuildSut(new RuntimeSettings
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-2.5-flash",
        });

        await router.AnalyzeAsync("sys", "{}", model: "gemini-2.5-flash-lite");

        openAiHandler.LastRequestBody.Should().Contain("\"model\":\"gemini-2.5-flash-lite\"");
    }

    [Fact]
    public async Task ScoreSentiment_uses_routine_model_from_settings()
    {
        var sentimentResponse = """
        {
          "model": "m",
          "choices": [
            { "message": { "content": null, "tool_calls": [
                { "id": "c1", "type": "function",
                  "function": { "name": "emit_sentiment_scores", "arguments": "{\"scores\":[]}" } } ] } }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1 }
        }
        """;
        var (router, _, openAiHandler) = BuildSut(new RuntimeSettings
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-2.5-flash",
            LlmRoutineModel = "gemini-2.5-flash-lite",
        });
        openAiHandler.ResponseBody = sentimentResponse;

        await router.ScoreSentimentAsync(new[] { "AAPL: news" });

        openAiHandler.LastRequestBody.Should().Contain("\"model\":\"gemini-2.5-flash-lite\"");
    }

    [Fact]
    public async Task Missing_gemini_key_throws_actionable_message()
    {
        var (router, _, _) = BuildSut(
            new RuntimeSettings { LlmProvider = "gemini" },
            new LlmOptions { GeminiApiKey = "" });

        var act = () => router.AnalyzeAsync("sys", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Gemini API key not configured*");
    }

    [Fact]
    public async Task Custom_provider_without_base_url_throws_actionable_message()
    {
        var (router, _, _) = BuildSut(new RuntimeSettings { LlmProvider = "custom" });

        var act = () => router.AnalyzeAsync("sys", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Custom LLM base URL not configured*");
    }
}
