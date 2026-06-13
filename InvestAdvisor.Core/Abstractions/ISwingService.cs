namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Orchestrates the swing module's daily work: fetch bars for the swing universe, rank, log the top
/// qualifying setups as paper trades, and resolve any open paper trades against fresh bars. Also
/// runs the backtest gate. Called by the swing worker; kept behind an interface so it can be driven
/// manually ("Run now") and unit-tested.
/// </summary>
public interface ISwingService
{
    /// <summary>
    /// One scan cycle: resolve open paper trades from new bars, then generate today's setups.
    /// Idempotent per day — re-running won't double-log setups for the same session.
    /// </summary>
    Task<int> GenerateSetupsAsync(bool force = false, CancellationToken ct = default);

    /// <summary>Replays the swing rule over the universe's history and persists the summary (the gate).</summary>
    Task RunBacktestAsync(CancellationToken ct = default);
}
