using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Provider-neutral LLM client. The DI-registered implementation routes each call to the
/// provider selected at runtime in Settings (Gemini free tier by default, Anthropic Claude,
/// or any custom OpenAI-compatible endpoint such as Groq/OpenRouter/Ollama).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends one portfolio-analysis request and returns the parsed result alongside the raw
    /// response body for AdviceLog persistence.
    /// </summary>
    /// <param name="model">
    /// Optional model-id override (e.g. a cheaper routine model). Falls back to the configured
    /// primary model when null/blank.
    /// </param>
    Task<LlmAnalysisResult> AnalyzeAsync(
        string systemPrompt,
        string runContextJson,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends one per-stock analysis request (screener) and returns the structured bull/bear case.
    /// Forces the <c>emit_stock_analysis</c> tool for a deterministic shape.
    /// </summary>
    Task<StockAnalysisResult> AnalyzeStockAsync(
        string systemPrompt,
        string stockContextJson,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>
    /// Single consolidated "where to invest today" call: takes the ranked candidates across all
    /// asset classes and returns a focused buy shortlist per class. Forces the
    /// <c>emit_daily_recommendation</c> tool.
    /// </summary>
    Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt,
        string candidatesContextJson,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>
    /// Scores a batch of news/social items for investor sentiment in one call. Each input is a short
    /// line (e.g. "AAPL: headline…"); the result carries one <see cref="SentimentScore"/> per input
    /// index. Forces the <c>emit_sentiment_scores</c> tool and uses the cheaper routine model.
    /// </summary>
    Task<SentimentBatchResult> ScoreSentimentAsync(
        IReadOnlyList<string> items,
        string? model = null,
        CancellationToken ct = default);
}

/// <summary>One scored item: <paramref name="Index"/> matches the input position, score is [-1, 1].</summary>
public sealed record SentimentScore(int Index, decimal Score, string Label);

public sealed record SentimentBatchResult(
    IReadOnlyList<SentimentScore> Scores,
    string RawResponseBody,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed);

public sealed record RecommendedPick(string Ticker, string Reason);

public sealed record DailyRecommendationResult(
    string Summary,
    string Caution,
    IReadOnlyList<RecommendedPick> Stocks,
    IReadOnlyList<RecommendedPick> Etfs,
    IReadOnlyList<RecommendedPick> Crypto,
    string RawResponseBody,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed);

public sealed record StockAnalysisResult(
    string Summary,
    string Thesis,
    IReadOnlyList<string> BullishFactors,
    IReadOnlyList<string> BearishFactors,
    IReadOnlyList<string> KeyRisks,
    int Conviction,
    string ConvictionLabel,
    string RawResponseBody,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed);

public sealed record LlmAnalysisResult(
    AgentAnalysis Analysis,
    string RawResponseBody,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed);

public sealed class AgentParseException(string message, string responseBody, Exception? inner = null)
    : Exception(message, inner)
{
    public string ResponseBody { get; } = responseBody;
}
