using System.Collections.Concurrent;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Runs;

/// <summary>
/// Singleton run orchestrator. Each <see cref="Start"/> spins the work onto the thread pool inside a
/// fresh DI scope (the background task has no Blazor circuit / HTTP scope) and owns its own
/// cancellation source — so a page tearing down on navigation can no longer cancel the run. Tenant +
/// kind acts as a one-at-a-time lock. Completion writes a bell notification and publishes
/// <see cref="RunCompletedEvent"/> so open pages refresh.
/// </summary>
public sealed class RunManager(
    IServiceScopeFactory scopeFactory,
    INotificationCenter notifications,
    IRunEventBus bus,
    ISystemClock clock,
    ILogger<RunManager> logger) : IRunManager
{
    private sealed class Job
    {
        public required CancellationTokenSource Cts { get; init; }
        public ActiveRun State { get; set; } = null!;
    }

    private readonly ConcurrentDictionary<(int TenantId, RunKind Kind), Job> _active = new();

    public event EventHandler<RunStateChangedEvent>? RunStateChanged;

    public bool Start(int tenantId, RunKind kind)
    {
        var job = new Job { Cts = new CancellationTokenSource() };
        if (!_active.TryAdd((tenantId, kind), job))
            return false; // already running this kind for this tenant

        job.State = new ActiveRun(tenantId, kind, InitialPhase(kind), clock.UtcNow, Canceling: false);
        Raise(job.State);
        _ = Task.Run(() => RunAsync(tenantId, kind, job));
        return true;
    }

    public ActiveRun? GetActive(int tenantId, RunKind kind)
        => _active.TryGetValue((tenantId, kind), out var job) ? job.State : null;

    public void Cancel(int tenantId, RunKind kind)
    {
        if (!_active.TryGetValue((tenantId, kind), out var job)) return;
        job.State = job.State with { Canceling = true };
        Raise(job.State);
        job.Cts.Cancel();
    }

    private async Task RunAsync(int tenantId, RunKind kind, Job job)
    {
        var ct = job.Cts.Token;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            switch (kind)
            {
                case RunKind.Dashboard:
                    await RunDashboardAsync(tenantId, job, sp, ct);
                    break;
                case RunKind.Swing:
                    await RunSwingAsync(tenantId, job, sp, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await SafeNotifyAsync(tenantId, new NotificationDraft(
                Title: $"{Label(kind)} canceled",
                Body: "You stopped the run before it finished.",
                Severity: NotificationSeverity.Info));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {Kind} failed for tenant {Tenant}.", kind, tenantId);
            await SafeNotifyAsync(tenantId, new NotificationDraft(
                Title: $"{Label(kind)} failed",
                Body: ex.Message,
                Severity: NotificationSeverity.Error));
        }
        finally
        {
            _active.TryRemove((tenantId, kind), out _);
            job.Cts.Dispose();
            RunStateChanged?.Invoke(this, new RunStateChangedEvent(tenantId, kind, Active: null));
        }
    }

    private async Task RunDashboardAsync(int tenantId, Job job, IServiceProvider sp, CancellationToken ct)
    {
        var agent = sp.GetRequiredService<IAgentService>();
        var recommender = sp.GetRequiredService<IDailyRecommendationService>();

        SetPhase(job, "Analyzing your holdings…");
        var adviceLogId = await agent.RunNowAsync(tenantId, "Dashboard Run now", ct);

        SetPhase(job, "Finding where to invest…");
        await recommender.GenerateAsync(tenantId, force: true, ct: ct);

        await notifications.AddAsync(tenantId, new NotificationDraft(
            Title: "Dashboard run complete",
            Body: "Your holdings advice and today's picks are updated.",
            Severity: NotificationSeverity.Success,
            LinkUrl: "/",
            AdviceLogId: adviceLogId), ct);

        // Existing live-refresh path: open pages reload their snapshot on this event.
        bus.Publish(new RunCompletedEvent(adviceLogId, clock.UtcNow, RunTriggerKind.Manual.ToString()));
    }

    private async Task RunSwingAsync(int tenantId, Job job, IServiceProvider sp, CancellationToken ct)
    {
        var swing = sp.GetRequiredService<ISwingService>();

        SetPhase(job, "Scanning the swing universe…");
        var count = await swing.GenerateSetupsAsync(force: true, ct);

        await notifications.AddAsync(tenantId, new NotificationDraft(
            Title: "Swing scan complete",
            Body: count > 0
                ? $"{count} qualifying setup{(count == 1 ? "" : "s")} found."
                : "No qualifying setups right now — check the watchlist.",
            Severity: count > 0 ? NotificationSeverity.Success : NotificationSeverity.Info,
            LinkUrl: "/swing"), ct);
    }

    private void SetPhase(Job job, string phase)
    {
        job.State = job.State with { Phase = phase };
        Raise(job.State);
    }

    private void Raise(ActiveRun state)
        => RunStateChanged?.Invoke(this, new RunStateChangedEvent(state.TenantId, state.Kind, state));

    /// <summary>Best-effort notification when the run is already failing — never throw from the catch.</summary>
    private async Task SafeNotifyAsync(int tenantId, NotificationDraft draft)
    {
        try { await notifications.AddAsync(tenantId, draft); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to write run notification for tenant {Tenant}.", tenantId); }
    }

    private static string InitialPhase(RunKind kind) => kind switch
    {
        RunKind.Dashboard => "Analyzing your holdings…",
        RunKind.Swing => "Scanning the swing universe…",
        _ => "Working…",
    };

    private static string Label(RunKind kind) => kind switch
    {
        RunKind.Dashboard => "Dashboard run",
        RunKind.Swing => "Swing scan",
        _ => "Run",
    };
}
