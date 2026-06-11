namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// In-process event bus so Blazor pages can live-refresh when the worker finishes a run
/// or refreshes market data.
/// </summary>
public interface IRunEventBus
{
    event EventHandler<RunCompletedEvent>? RunCompleted;
    event EventHandler<PricesRefreshedEvent>? PricesRefreshed;
    void Publish(RunCompletedEvent evt);
    void Publish(PricesRefreshedEvent evt);
}

public sealed record RunCompletedEvent(long AdviceLogId, DateTime TimestampUtc, string Trigger);

/// <summary>Raised after the worker writes fresh price snapshots (every tick, all tenants' tickers).</summary>
public sealed record PricesRefreshedEvent(IReadOnlyList<string> Tickers, DateTime TimestampUtc);
