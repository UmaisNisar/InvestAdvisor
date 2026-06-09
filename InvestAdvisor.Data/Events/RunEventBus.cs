using InvestAdvisor.Core.Abstractions;

namespace InvestAdvisor.Data.Events;

/// <summary>
/// Trivial in-process event bus. Registered as a singleton; Blazor components subscribe
/// in OnInitializedAsync and unsubscribe in Dispose to live-refresh after the worker
/// completes a run.
/// </summary>
public sealed class RunEventBus : IRunEventBus
{
    public event EventHandler<RunCompletedEvent>? RunCompleted;
    public void Publish(RunCompletedEvent evt) => RunCompleted?.Invoke(this, evt);
}
