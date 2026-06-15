using InvestAdvisor.Core.Momentum;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Ranks the high-volatility universe for a short-horizon breakout entry. Like the swing scorer it
/// percentile-ranks each factor across the universe, but the factors are momentum/expansion ones
/// (breakout strength, base tightness, volume surge, volatility, trailing momentum) plus optional
/// sentiment. Pure and synchronous: the caller fetches the bars, this just scores them.
/// </summary>
public interface IMomentumScoringService
{
    /// <summary>
    /// Scores every input with enough bars, best-first. <paramref name="sentimentByTicker"/> is the
    /// mean sentiment per ticker (optional confirming factor). Names that don't meet the breakout
    /// conditions are still returned (ranked low) but flagged <see cref="MomentumScore.Qualifies"/>=false.
    /// </summary>
    IReadOnlyList<MomentumScore> Rank(
        IReadOnlyList<MomentumInput> universe,
        IReadOnlyDictionary<string, decimal>? sentimentByTicker = null,
        MomentumParams? parameters = null);
}
