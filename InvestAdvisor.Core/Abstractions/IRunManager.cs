using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Runs a manual "Run now" off the UI thread so it survives the user navigating away. Singleton:
/// it owns the work's <see cref="CancellationTokenSource"/> (not the page), so leaving the page no
/// longer cancels the run. On completion it writes a bell notification and live-refreshes open pages.
/// </summary>
public interface IRunManager
{
    /// <summary>Fires whenever a run starts, changes phase, or finishes — so pages can show progress.</summary>
    event EventHandler<RunStateChangedEvent>? RunStateChanged;

    /// <summary>Starts a run for the tenant. Returns false if one of this kind is already in flight.</summary>
    bool Start(int tenantId, RunKind kind);

    /// <summary>The in-flight run for this tenant+kind, or null if none is running.</summary>
    ActiveRun? GetActive(int tenantId, RunKind kind);

    /// <summary>Requests cancellation of an in-flight run (the user pressed Cancel).</summary>
    void Cancel(int tenantId, RunKind kind);
}

/// <summary>A run currently executing in the background.</summary>
public sealed record ActiveRun(int TenantId, RunKind Kind, string Phase, DateTime StartedUtc, bool Canceling);

/// <summary>Raised on every run state transition. <paramref name="Active"/> is null when the run ended.</summary>
public sealed record RunStateChangedEvent(int TenantId, RunKind Kind, ActiveRun? Active);
