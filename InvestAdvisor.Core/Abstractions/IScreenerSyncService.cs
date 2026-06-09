namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Fetches fundamentals + analyst ratings + insider trades for the active universe and persists
/// new snapshots. Idempotent per day for ratings/insider; metrics append one snapshot per run.
/// </summary>
public interface IScreenerSyncService
{
    Task<ScreenerSyncResult> SyncAsync(CancellationToken ct = default);
}

public sealed record ScreenerSyncResult(
    int StocksProcessed,
    int MetricsWritten,
    int RatingsWritten,
    int InsiderTradesWritten);
