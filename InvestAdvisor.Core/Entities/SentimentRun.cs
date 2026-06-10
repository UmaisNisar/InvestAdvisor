namespace InvestAdvisor.Core.Entities;

/// <summary>
/// One batch sentiment-scoring LLM call. Carries the model + token columns so
/// <c>CostService</c> counts it toward the daily budget alongside the other run rows
/// (AdviceLog, DailyRecommendation, StockAnalysis). No separate cost table — runs are the history.
/// </summary>
public class SentimentRun
{
    public long Id { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>How many news/social items this batch scored.</summary>
    public int ItemsScored { get; set; }

    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
}
