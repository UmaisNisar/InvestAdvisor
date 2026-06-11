namespace InvestAdvisor.Data.Providers;

/// <summary>
/// Static prompt text shared by all LLM provider clients (Anthropic + OpenAI-compatible).
/// Kept in one place so prompt tweaks don't drift between providers.
/// </summary>
internal static class LlmEnvelope
{
    public const string AnalysisUserPreamble =
        "Here is the current structured context for the portfolio as JSON. " +
        "Call the emit_analysis tool exactly once to return your structured response.\n\n";

    public const string StockUserPreamble =
        "Analyze this single stock from the structured data below. " +
        "Call the emit_stock_analysis tool exactly once.\n\n";

    public const string RecommendationUserPreamble =
        "From the ranked candidates below, choose where to invest today in each " +
        "asset class. Call the emit_daily_recommendation tool exactly once.\n\n";

    public const string SentimentSystemPrompt =
        "You are a financial-sentiment classifier. For each numbered item you read the headline or " +
        "post and judge investor sentiment toward the security/asset it concerns. Output is a read of " +
        "tone only — never a price prediction or a trade recommendation. Treat purely factual or " +
        "procedural items as neutral (0). Call the emit_sentiment_scores tool exactly once with one " +
        "entry per input index.";

    public static string BuildSentimentUserMessage(IReadOnlyList<string> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Score the investor sentiment of each item below. Return one entry per index.\n\n");
        for (var i = 0; i < items.Count; i++)
            sb.Append('[').Append(i).Append("] ").Append(items[i]).Append('\n');
        return sb.ToString();
    }
}
