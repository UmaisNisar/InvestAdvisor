using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.OpenAiCompat;

/// <summary>
/// Where a chat-completions request goes: resolved per call by the router so the provider can be
/// switched at runtime (Gemini free tier vs any custom OpenAI-compatible endpoint).
/// </summary>
public readonly record struct LlmEndpoint(string BaseUrl, string ApiKey, string ProviderLabel);

/// <summary>
/// Generic OpenAI-compatible chat-completions client. One implementation covers Google Gemini's
/// OpenAI-compat endpoint (the free default), Groq, OpenRouter, Ollama, and anything else that
/// speaks the same protocol — the router supplies the endpoint per call. Mirrors the Anthropic
/// client: forces the relevant <c>emit_*</c> function via <c>tool_choice</c>, then parses through
/// the shared <see cref="LlmResponseParsing"/> (tool-call happy path + balanced-JSON text fallback).
/// </summary>
public sealed class OpenAiCompatibleClient(
    HttpClient http,
    IOptions<LlmOptions> options,
    ILogger<OpenAiCompatibleClient>? logger = null)
{
    private readonly LlmOptions _opts = options.Value;

    public async Task<LlmAnalysisResult> AnalyzeAsync(
        LlmEndpoint endpoint,
        string model,
        string systemPrompt,
        string runContextJson,
        CancellationToken ct = default)
    {
        var body = BuildBody(model, systemPrompt,
            LlmEnvelope.AnalysisUserPreamble + runContextJson,
            EmitAnalysisToolSchema.AsToolNode(), EmitAnalysisToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(endpoint, body, ct);
        var responseModel = parsed.Model ?? model;
        var inputTokens = parsed.Usage?.PromptTokens ?? 0;
        var outputTokens = parsed.Usage?.CompletionTokens ?? 0;

        using var payload = ExtractPayload(parsed, EmitAnalysisToolSchema.ToolName);
        var (analysis, fallbackUsed) = LlmResponseParsing.Parse(
            payload.ToolInput, payload.Text, rawBody, EmitAnalysisToolSchema.ToolName,
            el => LlmResponseParsing.DeserializeAnalysis(el, responseModel, inputTokens, outputTokens));
        analysis = analysis with { Metrics = analysis.Metrics with { ParseFallbackUsed = fallbackUsed } };

        return new LlmAnalysisResult(
            Analysis: analysis,
            RawResponseBody: rawBody,
            Model: responseModel,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<StockAnalysisResult> AnalyzeStockAsync(
        LlmEndpoint endpoint,
        string model,
        string systemPrompt,
        string stockContextJson,
        CancellationToken ct = default)
    {
        var body = BuildBody(model, systemPrompt,
            LlmEnvelope.StockUserPreamble + stockContextJson,
            EmitStockAnalysisToolSchema.AsToolNode(), EmitStockAnalysisToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(endpoint, body, ct);

        using var payload = ExtractPayload(parsed, EmitStockAnalysisToolSchema.ToolName);
        var (s, fallbackUsed) = LlmResponseParsing.Parse(
            payload.ToolInput, payload.Text, rawBody, EmitStockAnalysisToolSchema.ToolName,
            LlmResponseParsing.DeserializeStock);

        return new StockAnalysisResult(
            Summary: s.Summary,
            Thesis: s.Thesis,
            BullishFactors: s.Bullish,
            BearishFactors: s.Bearish,
            KeyRisks: s.Risks,
            Conviction: s.Conviction,
            ConvictionLabel: s.ConvictionLabel,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? model,
            InputTokens: parsed.Usage?.PromptTokens ?? 0,
            OutputTokens: parsed.Usage?.CompletionTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<DailyRecommendationResult> RecommendAllocationAsync(
        LlmEndpoint endpoint,
        string model,
        string systemPrompt,
        string candidatesContextJson,
        CancellationToken ct = default)
    {
        var body = BuildBody(model, systemPrompt,
            LlmEnvelope.RecommendationUserPreamble + candidatesContextJson,
            EmitDailyRecommendationToolSchema.AsToolNode(), EmitDailyRecommendationToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(endpoint, body, ct);

        using var payload = ExtractPayload(parsed, EmitDailyRecommendationToolSchema.ToolName);
        var (rec, fallbackUsed) = LlmResponseParsing.Parse(
            payload.ToolInput, payload.Text, rawBody, EmitDailyRecommendationToolSchema.ToolName,
            LlmResponseParsing.DeserializeRecommendation);

        return new DailyRecommendationResult(
            Summary: rec.Summary,
            Caution: rec.Caution,
            Stocks: rec.Stocks,
            Etfs: rec.Etfs,
            Crypto: rec.Crypto,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? model,
            InputTokens: parsed.Usage?.PromptTokens ?? 0,
            OutputTokens: parsed.Usage?.CompletionTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<SentimentBatchResult> ScoreSentimentAsync(
        LlmEndpoint endpoint,
        string model,
        IReadOnlyList<string> items,
        CancellationToken ct = default)
    {
        var body = BuildBody(model, LlmEnvelope.SentimentSystemPrompt,
            LlmEnvelope.BuildSentimentUserMessage(items),
            EmitSentimentScoresToolSchema.AsToolNode(), EmitSentimentScoresToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(endpoint, body, ct);

        using var payload = ExtractPayload(parsed, EmitSentimentScoresToolSchema.ToolName);
        var (scores, fallbackUsed) = LlmResponseParsing.Parse(
            payload.ToolInput, payload.Text, rawBody, EmitSentimentScoresToolSchema.ToolName,
            LlmResponseParsing.DeserializeSentiment);

        return new SentimentBatchResult(
            Scores: scores,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? model,
            InputTokens: parsed.Usage?.PromptTokens ?? 0,
            OutputTokens: parsed.Usage?.CompletionTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    private JsonObject BuildBody(
        string model, string systemPrompt, string userContent, JsonObject toolNode, string toolName) => new()
    {
        ["model"] = model,
        ["max_tokens"] = _opts.MaxTokens,
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = userContent },
        },
        ["tools"] = new JsonArray { OpenAiToolConverter.ToFunctionTool(toolNode) },
        ["tool_choice"] = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = toolName },
        },
    };

    private async Task<(OpenAiChatResponse Parsed, string RawBody, int LatencyMs)> SendAsync(
        LlmEndpoint endpoint, JsonObject body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
            throw new InvalidOperationException(
                $"{endpoint.ProviderLabel} base URL not configured. Set it in Settings → AI Provider.");

        // Relative join must preserve any path segment in the base URL (Gemini's /v1beta/openai/),
        // so ensure a trailing slash and never lead with '/'.
        var baseUrl = endpoint.BaseUrl.EndsWith('/') ? endpoint.BaseUrl : endpoint.BaseUrl + "/";
        var uri = new Uri(new Uri(baseUrl), "chat/completions");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { WriteIndented = false }),
        };
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);

        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogError("{Provider} API call failed: {Status} {Body}",
                endpoint.ProviderLabel, response.StatusCode, rawBody);
            throw new HttpRequestException(
                $"{endpoint.ProviderLabel} API returned {(int)response.StatusCode}: {rawBody}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(rawBody)
            ?? throw new AgentParseException($"{endpoint.ProviderLabel} response body deserialized to null.", rawBody);

        return (parsed, rawBody, (int)sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Holds the parsed tool-call arguments document alive while the caller deserializes from it.
    /// </summary>
    private readonly struct ResponsePayload(JsonDocument? argsDoc, JsonElement? toolInput, string text) : IDisposable
    {
        public JsonElement? ToolInput { get; } = toolInput;
        public string Text { get; } = text;
        public void Dispose() => argsDoc?.Dispose();
    }

    private static ResponsePayload ExtractPayload(OpenAiChatResponse response, string toolName)
    {
        var message = response.Choices.FirstOrDefault()?.Message;
        var text = message?.ContentText ?? string.Empty;

        // Prefer the forced function by name, but accept any single tool call — some providers
        // mangle the name while still honoring the schema.
        var call = message?.ToolCalls?.FirstOrDefault(c =>
                       string.Equals(c.Function?.Name, toolName, StringComparison.Ordinal))
                   ?? message?.ToolCalls?.FirstOrDefault();

        if (call?.Function?.Arguments is { Length: > 0 } args)
        {
            try
            {
                var doc = JsonDocument.Parse(args);
                return new ResponsePayload(doc, doc.RootElement, text);
            }
            catch (JsonException)
            {
                // Malformed arguments — fall through to the text fallback.
            }
        }

        return new ResponsePayload(null, null, text);
    }
}
