using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using InvestAdvisor.Core.Abstractions;
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
/// speaks the same protocol — the router supplies the endpoint per call. Forces the relevant
/// <c>emit_*</c> function via <c>tool_choice</c>; parsing is shared with the Anthropic client
/// through <see cref="LlmClientBase{TContext}"/>.
/// </summary>
public sealed class OpenAiCompatibleClient(
    HttpClient http,
    IOptions<LlmOptions> options,
    ILogger<OpenAiCompatibleClient>? logger = null) : LlmClientBase<LlmEndpoint>
{
    private readonly LlmOptions _opts = options.Value;

    public Task<LlmAnalysisResult> AnalyzeAsync(
        LlmEndpoint endpoint, string model, string systemPrompt, string runContextJson, CancellationToken ct = default) =>
        AnalyzeCoreAsync(endpoint, model, systemPrompt, runContextJson, ct);

    public Task<DailyRecommendationResult> RecommendAllocationAsync(
        LlmEndpoint endpoint, string model, string systemPrompt, string candidatesContextJson, CancellationToken ct = default) =>
        RecommendCoreAsync(endpoint, model, systemPrompt, candidatesContextJson, ct);

    public Task<SentimentBatchResult> ScoreSentimentAsync(
        LlmEndpoint endpoint, string model, IReadOnlyList<string> items, CancellationToken ct = default) =>
        ScoreSentimentCoreAsync(endpoint, model, items, ct);

    protected override async Task<LlmReply> InvokeAsync(
        LlmEndpoint endpoint, string model, string systemPrompt, string userContent,
        JsonObject toolNode, string toolName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
            throw new InvalidOperationException(
                $"{endpoint.ProviderLabel} base URL not configured. Set it in Settings → AI Provider.");

        var body = new JsonObject
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

        // Relative join must preserve any path segment in the base URL (Gemini's /v1beta/openai/),
        // so ensure a trailing slash and never lead with '/'.
        var baseUrl = endpoint.BaseUrl.EndsWith('/') ? endpoint.BaseUrl : endpoint.BaseUrl + "/";
        var uri = new Uri(new Uri(baseUrl), "chat/completions");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: BodyJson),
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

        var message = parsed.Choices.FirstOrDefault()?.Message;
        var text = message?.ContentText ?? string.Empty;

        // Prefer the forced function by name, but accept any single tool call — some providers
        // mangle the name while still honoring the schema.
        var call = message?.ToolCalls?.FirstOrDefault(c =>
                       string.Equals(c.Function?.Name, toolName, StringComparison.Ordinal))
                   ?? message?.ToolCalls?.FirstOrDefault();

        JsonDocument? argsDoc = null;
        if (call?.Function?.Arguments is { Length: > 0 } args)
        {
            try { argsDoc = JsonDocument.Parse(args); }
            catch (JsonException) { /* malformed arguments — fall through to the text fallback */ }
        }

        return new LlmReply(argsDoc?.RootElement, text, rawBody, parsed.Model ?? model,
            parsed.Usage?.PromptTokens ?? 0, parsed.Usage?.CompletionTokens ?? 0,
            (int)sw.ElapsedMilliseconds, Holder: argsDoc);
    }
}
