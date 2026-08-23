using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Anthropic;

/// <summary>
/// Typed HttpClient against the Anthropic Messages API (paid Claude models). Forces
/// <c>tool_choice</c> to the relevant <c>emit_*</c> tool on every request so the response shape
/// is deterministic; the shared <see cref="LlmClientBase{TContext}"/> falls back to extracting
/// the first balanced JSON object from text content when the model fails to return a
/// <c>tool_use</c> block.
/// </summary>
public sealed class AnthropicClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicClient>? logger = null) : LlmClientBase<object?>, ILlmClient
{
    private readonly AnthropicOptions _opts = options.Value;

    public Task<LlmAnalysisResult> AnalyzeAsync(
        string systemPrompt, string runContextJson, string? model = null, CancellationToken ct = default) =>
        AnalyzeCoreAsync(null, Resolve(model, _opts.Model), systemPrompt, runContextJson, ct);

    public Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt, string candidatesContextJson, string? model = null, CancellationToken ct = default) =>
        RecommendCoreAsync(null, Resolve(model, _opts.Model), systemPrompt, candidatesContextJson, ct);

    public Task<SentimentBatchResult> ScoreSentimentAsync(
        IReadOnlyList<string> items, string? model = null, CancellationToken ct = default) =>
        ScoreSentimentCoreAsync(null, Resolve(model, _opts.RoutineModel), items, ct);

    private static string Resolve(string? model, string fallback) =>
        string.IsNullOrWhiteSpace(model) ? fallback : model;

    protected override async Task<LlmReply> InvokeAsync(
        object? _, string model, string systemPrompt, string userContent,
        JsonObject toolNode, string toolName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Anthropic API key not configured. Set Anthropic:ApiKey via user-secrets or the ANTHROPIC_API_KEY env var, " +
                "or switch to a free provider in Settings → AI Provider.");

        var body = new JsonObject
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(body, options: BodyJson),
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

        var toolUse = parsed.Content.FirstOrDefault(c =>
            string.Equals(c.Type, "tool_use", StringComparison.Ordinal) &&
            string.Equals(c.Name, toolName, StringComparison.Ordinal));
        JsonElement? input = toolUse is not null && toolUse.Input.ValueKind == JsonValueKind.Object
            ? toolUse.Input
            : null;
        var text = string.Join('\n',
            parsed.Content.Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text)).Select(c => c.Text));

        return new LlmReply(input, text, rawBody, parsed.Model ?? model,
            parsed.Usage?.InputTokens ?? 0, parsed.Usage?.OutputTokens ?? 0, (int)sw.ElapsedMilliseconds);
    }
}
