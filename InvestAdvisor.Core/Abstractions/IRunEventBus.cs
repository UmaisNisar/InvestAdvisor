namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// In-process event bus so Blazor pages can live-refresh when the worker finishes a run.
/// </summary>
public interface IRunEventBus
{
    event EventHandler<RunCompletedEvent>? RunCompleted;
    void Publish(RunCompletedEvent evt);
}

public sealed record RunCompletedEvent(long AdviceLogId, DateTime TimestampUtc, string Trigger);
