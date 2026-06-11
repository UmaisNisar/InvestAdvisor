using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Anthropic;

/// <summary>
/// Typed HttpClient against the Anthropic Messages API (paid Claude models). Forces
/// <c>tool_choice</c> to the relevant <c>emit_*</c> tool on every request so the response shape
/// is deterministic; falls back to extracting the first balanced JSON object from text content
/// when the model fails to return a <c>tool_use</c> block. Parsing is shared with the
/// OpenAI-compatible client via <see cref="LlmResponseParsing"/>.
/// </summary>
public sealed class AnthropicClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicClient>? logger = null) : ILlmClient
{
    private readonly AnthropicOptions _opts = options.Value;

    public async Task<LlmAnalysisResult> AnalyzeAsync(
        string systemPrompt,
        string runContextJson,
        string? model = null,
        CancellationToken ct = default)
    {
        var resolvedModel = Resolve(model, _opts.Model);
        var body = BuildBody(resolvedModel, systemPrompt,
            LlmEnvelope.AnalysisUserPreamble + runContextJson, EmitAnalysisToolSchema.AsToolNode(),
            EmitAnalysisToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (toolInput, text) = ExtractPayload(parsed, EmitAnalysisToolSchema.ToolName);

        var responseModel = parsed.Model ?? resolvedModel;
        var (analysis, fallbackUsed) = LlmResponseParsing.Parse(
            toolInput, text, rawBody, EmitAnalysisToolSchema.ToolName,
            el => LlmResponseParsing.DeserializeAnalysis(
                el, responseModel, parsed.Usage?.InputTokens ?? 0, parsed.Usage?.OutputTokens ?? 0));
        analysis = analysis with { Metrics = analysis.Metrics with { ParseFallbackUsed = fallbackUsed } };

        return new LlmAnalysisResult(
            Analysis: analysis,
            RawResponseBody: rawBody,
            Model: responseModel,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<StockAnalysisResult> AnalyzeStockAsync(
        string systemPrompt,
        string stockContextJson,
        string? model = null,
        CancellationToken ct = default)
    {
        var resolvedModel = Resolve(model, _opts.Model);
        var body = BuildBody(resolvedModel, systemPrompt,
            LlmEnvelope.StockUserPreamble + stockContextJson, EmitStockAnalysisToolSchema.AsToolNode(),
            EmitStockAnalysisToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (toolInput, text) = ExtractPayload(parsed, EmitStockAnalysisToolSchema.ToolName);
        var (s, fallbackUsed) = LlmResponseParsing.Parse(
            toolInput, text, rawBody, EmitStockAnalysisToolSchema.ToolName, LlmResponseParsing.DeserializeStock);

        return new StockAnalysisResult(
            Summary: s.Summary,
            Thesis: s.Thesis,
            BullishFactors: s.Bullish,
            BearishFactors: s.Bearish,
            KeyRisks: s.Risks,
            Conviction: s.Conviction,
            ConvictionLabel: s.ConvictionLabel,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? resolvedModel,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt,
        string candidatesContextJson,
        string? model = null,
        CancellationToken ct = default)
    {
        var resolvedModel = Resolve(model, _opts.Model);
        var body = BuildBody(resolvedModel, systemPrompt,
            LlmEnvelope.RecommendationUserPreamble + candidatesContextJson,
            EmitDailyRecommendationToolSchema.AsToolNode(), EmitDailyRecommendationToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (toolInput, text) = ExtractPayload(parsed, EmitDailyRecommendationToolSchema.ToolName);
        var (rec, fallbackUsed) = LlmResponseParsing.Parse(
            toolInput, text, rawBody, EmitDailyRecommendationToolSchema.ToolName,
            LlmResponseParsing.DeserializeRecommendation);

        return new DailyRecommendationResult(
            Summary: rec.Summary,
            Caution: rec.Caution,
            Stocks: rec.Stocks,
            Etfs: rec.Etfs,
            Crypto: rec.Crypto,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? resolvedModel,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<SentimentBatchResult> ScoreSentimentAsync(
        IReadOnlyList<string> items,
        string? model = null,
        CancellationToken ct = default)
    {
        var resolvedModel = Resolve(model, _opts.RoutineModel);
        var body = BuildBody(resolvedModel, LlmEnvelope.SentimentSystemPrompt,
            LlmEnvelope.BuildSentimentUserMessage(items), EmitSentimentScoresToolSchema.AsToolNode(),
            EmitSentimentScoresToolSchema.ToolName);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (toolInput, text) = ExtractPayload(parsed, EmitSentimentScoresToolSchema.ToolName);
        var (scores, fallbackUsed) = LlmResponseParsing.Parse(
            toolInput, text, rawBody, EmitSentimentScoresToolSchema.ToolName,
            LlmResponseParsing.DeserializeSentiment);

        return new SentimentBatchResult(
            Scores: scores,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? resolvedModel,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    private static string Resolve(string? model, string fallback) =>
        string.IsNullOrWhiteSpace(model) ? fallback : model;

    private JsonObject BuildBody(
        string model, string systemPrompt, string userContent, JsonObject toolNode, string toolName) => new()
    {
        ["model"] = model,
        ["max_tokens"] = _opts.MaxTokens,
        ["system"] = systemPrompt,
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = userContent },
        },
        ["tools"] = new JsonArray { toolNode },
        ["tool_choice"] = new JsonObject
        {
            ["type"] = "tool",
            ["name"] = toolName,
        },
    };

    /// <summary>Pulls the matching tool_use input plus the concatenated text blocks out of the response.</summary>
    private static (JsonElement? ToolInput, string Text) ExtractPayload(
        AnthropicMessageResponse response, string toolName)
    {
        var toolUse = response.Content.FirstOrDefault(c =>
            string.Equals(c.Type, "tool_use", StringComparison.Ordinal) &&
            string.Equals(c.Name, toolName, StringComparison.Ordinal));

        JsonElement? input = toolUse is not null && toolUse.Input.ValueKind == JsonValueKind.Object
            ? toolUse.Input
            : null;

        var text = string.Join('\n',
            response.Content.Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text)).Select(c => c.Text));

        return (input, text);
    }

    private async Task<(AnthropicMessageResponse Parsed, string RawBody, int LatencyMs)> SendAsync(
        JsonObject body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Anthropic API key not configured. Set Anthropic:ApiKey via user-secrets or the ANTHROPIC_API_KEY env var, " +
                "or switch to a free provider in Settings → AI Provider.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { WriteIndented = false }),
        };

        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogError("Anthropic API call failed: {Status} {Body}", response.StatusCode, rawBody);
            throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {rawBody}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicMessageResponse>(rawBody)
            ?? throw new AgentParseException("Anthropic response body deserialized to null.", rawBody);

        return (parsed, rawBody, (int)sw.ElapsedMilliseconds);
    }
}
