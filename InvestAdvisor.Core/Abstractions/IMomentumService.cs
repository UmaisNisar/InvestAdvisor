namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Orchestrates the high-vol momentum module: fetch bars for the momentum universe, rank, and persist
/// the top qualifying breakout setups as today's candidates; and replay the rule over history to
/// persist the backtest gate. Called by the momentum worker; behind an interface so it can be driven
/// manually ("Re-scan now") and unit-tested.
/// </summary>
public interface IMomentumService
{
    /// <summary>
    /// One scan cycle: rank the universe and replace today's candidate snapshot with the top
    /// qualifying breakout setups. Returns how many were surfaced. Idempotent per day unless forced.
    /// </summary>
    Task<int> GenerateSetupsAsync(bool force = false, CancellationToken ct = default);

    /// <summary>Replays the momentum rule over the universe's history and persists the summary (the gate).</summary>
    Task RunBacktestAsync(CancellationToken ct = default);
}
