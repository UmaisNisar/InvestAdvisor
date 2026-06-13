using InvestAdvisor.Core.Swing;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Ranks the swing universe for a short-horizon (2–3 day) entry. Like the fundamental screener it
/// percentile-ranks each factor across the universe, but the factors are technical (trend,
/// momentum, breakout, RSI position, relative volume) plus optional news/social sentiment. Pure and
/// synchronous: the caller fetches the bars, this just scores them, so it's trivially testable.
/// </summary>
public interface ISwingScoringService
{
    /// <summary>
    /// Scores every input with enough bars, best-first. <paramref name="sentimentByTicker"/> is the
    /// mean sentiment per ticker (optional confirming factor). Names that don't meet the long-entry
    /// conditions are still returned (ranked low) but flagged <see cref="SwingScore.Qualifies"/>=false.
    /// </summary>
    IReadOnlyList<SwingScore> Rank(
        IReadOnlyList<SwingInput> universe,
        IReadOnlyDictionary<string, decimal>? sentimentByTicker = null,
        SwingParams? parameters = null);
}
