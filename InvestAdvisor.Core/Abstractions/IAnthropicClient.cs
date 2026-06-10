using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IAnthropicClient
{
    /// <summary>
    /// Sends one analysis request to the Anthropic Messages API and returns the parsed result
    /// alongside the raw response body for AdviceLog persistence.
    /// </summary>
    /// <param name="model">
    /// Optional model-id override (e.g. a cheaper routine model). Falls back to the configured
    /// primary model when null/blank.
    /// </param>
    Task<AnthropicAnalysisResult> AnalyzeAsync(
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
        CancellationToken ct = default);

    /// <summary>
    /// Single consolidated "where to invest today" call: takes the ranked candidates across all
    /// asset classes and returns a focused buy shortlist per class. Forces the
    /// <c>emit_daily_recommendation</c> tool.
    /// </summary>
    Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt,
        string candidatesContextJson,
        CancellationToken ct = default);
}

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

public sealed record AnthropicAnalysisResult(
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
