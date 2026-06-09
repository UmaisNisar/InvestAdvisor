namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Produces the single daily "where to invest today" recommendation: one consolidated LLM call
/// over the top-ranked candidates in every asset class. Idempotent per day.
/// </summary>
public interface IDailyRecommendationService
{
    /// <summary>
    /// Generates today's "where to invest" recommendation. Idempotent per day unless
    /// <paramref name="force"/> is true (a manual "Run now" regenerates it).
    /// </summary>
    Task<bool> GenerateAsync(bool force = false, CancellationToken ct = default);
}
