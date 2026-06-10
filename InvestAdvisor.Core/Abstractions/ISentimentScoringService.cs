namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Grades the investor sentiment of ingested news/social items (LLM batch scoring) and exposes a
/// per-ticker aggregate that feeds both the screener's sentiment factor and the LLM run context.
/// </summary>
public interface ISentimentScoringService
{
    /// <summary>
    /// Scores items that have no sentiment yet, in batches. Idempotent — only touches unscored rows.
    /// Returns the number scored. No-op (returns 0) when the agent is paused or over the daily budget.
    /// </summary>
    Task<int> ScoreUnscoredAsync(CancellationToken ct = default);

    /// <summary>
    /// Recency-weighted mean sentiment per ticker over the recent window, keyed case-insensitively.
    /// Only scored rows count. Tickers with no scored items are absent from the map.
    /// </summary>
    Task<IReadOnlyDictionary<string, TickerSentiment>> GetTickerSentimentAsync(CancellationToken ct = default);
}

/// <summary>Aggregate sentiment for one ticker: <paramref name="MeanScore"/> in [-1, 1].</summary>
public sealed record TickerSentiment(decimal MeanScore, int PostCount, string Label);
