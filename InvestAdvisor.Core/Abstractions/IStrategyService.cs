using InvestAdvisor.Core.Trading;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Orchestrates a strategy's daily work: fetch bars for its universe, rank, log the top qualifying
/// setups as paper trades, resolve open paper trades against fresh bars, and replay the rule over
/// history for the backtest gate. Called by the strategy worker; behind an interface so it can be
/// driven manually ("Re-scan now") and unit-tested.
/// </summary>
public interface IStrategyService
{
    /// <summary>
    /// One scan cycle for <paramref name="kind"/>: resolve open paper trades from new bars, then
    /// generate today's setups. Idempotent per day unless <paramref name="force"/>, which replaces
    /// today's still-open setups at current levels (resolved trades are never touched).
    /// </summary>
    Task<int> GenerateSetupsAsync(StrategyKind kind, bool force = false, CancellationToken ct = default);

    /// <summary>Replays the strategy's rule over its universe's history and persists the summary (the gate).</summary>
    Task RunBacktestAsync(StrategyKind kind, CancellationToken ct = default);
}
