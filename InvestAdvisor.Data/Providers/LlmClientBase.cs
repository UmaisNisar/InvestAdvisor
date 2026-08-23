using System.Text.Json;
using System.Text.Json.Nodes;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// What a provider hands back from one forced-tool call, independent of wire format: the
/// tool-call input (null when the model didn't produce one), any plain-text content for the
/// JSON fallback, the raw body for persistence, and usage. <paramref name="Holder"/> keeps a
/// backing <see cref="JsonDocument"/> alive until the caller has deserialized from
/// <paramref name="ToolInput"/>.
/// </summary>
public readonly record struct LlmReply(
    JsonElement? ToolInput,
    string Text,
    string RawBody,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    IDisposable? Holder = null) : IDisposable
{
    public void Dispose() => Holder?.Dispose();
}

/// <summary>
/// The provider-neutral half of an LLM client: builds each <c>emit_*</c> request, parses the
/// reply through <see cref="LlmResponseParsing"/>, and maps it to the public result records.
/// Subclasses implement only <see cref="InvokeAsync"/> for their wire format.
/// <typeparamref name="TContext"/> is whatever per-call routing the subclass needs (the
/// OpenAI-compatible client takes an endpoint per call; Anthropic needs nothing).
/// </summary>
public abstract class LlmClientBase<TContext>
{
    /// <summary>Sends one forced-tool request and returns the normalised reply.</summary>
    protected abstract Task<LlmReply> InvokeAsync(
        TContext context, string model, string systemPrompt, string userContent,
        JsonObject toolNode, string toolName, CancellationToken ct);

    protected async Task<LlmAnalysisResult> AnalyzeCoreAsync(
        TContext context, string model, string systemPrompt, string runContextJson, CancellationToken ct)
    {
        using var reply = await InvokeAsync(context, model, systemPrompt,
            LlmEnvelope.AnalysisUserPreamble + runContextJson,
            EmitAnalysisToolSchema.AsToolNode(), EmitAnalysisToolSchema.ToolName, ct);

        var (analysis, fallbackUsed) = LlmResponseParsing.Parse(
            reply.ToolInput, reply.Text, reply.RawBody, EmitAnalysisToolSchema.ToolName,
            el => LlmResponseParsing.DeserializeAnalysis(el, reply.Model, reply.InputTokens, reply.OutputTokens));
        analysis = analysis with { Metrics = analysis.Metrics with { ParseFallbackUsed = fallbackUsed } };

        return new LlmAnalysisResult(analysis, reply.RawBody, reply.Model,
            reply.InputTokens, reply.OutputTokens, reply.LatencyMs, fallbackUsed);
    }

    protected async Task<DailyRecommendationResult> RecommendCoreAsync(
        TContext context, string model, string systemPrompt, string candidatesContextJson, CancellationToken ct)
    {
        using var reply = await InvokeAsync(context, model, systemPrompt,
            LlmEnvelope.RecommendationUserPreamble + candidatesContextJson,
            EmitDailyRecommendationToolSchema.AsToolNode(), EmitDailyRecommendationToolSchema.ToolName, ct);

        var (rec, fallbackUsed) = LlmResponseParsing.Parse(
            reply.ToolInput, reply.Text, reply.RawBody, EmitDailyRecommendationToolSchema.ToolName,
            LlmResponseParsing.DeserializeRecommendation);

        return new DailyRecommendationResult(rec.Summary, rec.Caution, rec.Etfs, rec.Crypto,
            reply.RawBody, reply.Model, reply.InputTokens, reply.OutputTokens, reply.LatencyMs, fallbackUsed);
    }

    protected async Task<SentimentBatchResult> ScoreSentimentCoreAsync(
        TContext context, string model, IReadOnlyList<string> items, CancellationToken ct)
    {
        using var reply = await InvokeAsync(context, model, LlmEnvelope.SentimentSystemPrompt,
            LlmEnvelope.BuildSentimentUserMessage(items),
            EmitSentimentScoresToolSchema.AsToolNode(), EmitSentimentScoresToolSchema.ToolName, ct);

        var (scores, fallbackUsed) = LlmResponseParsing.Parse(
            reply.ToolInput, reply.Text, reply.RawBody, EmitSentimentScoresToolSchema.ToolName,
            LlmResponseParsing.DeserializeSentiment);

        return new SentimentBatchResult(scores, reply.RawBody, reply.Model,
            reply.InputTokens, reply.OutputTokens, reply.LatencyMs, fallbackUsed);
    }

    /// <summary>Compact body serialization shared by both wire formats.</summary>
    protected static readonly JsonSerializerOptions BodyJson = new() { WriteIndented = false };
}
