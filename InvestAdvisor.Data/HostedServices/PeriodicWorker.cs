using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// The loop every background worker shares: wait out a startup delay, run an optional one-time
/// startup step, then tick forever — a failed tick is logged and retried next interval, and
/// cancellation ends the loop quietly. Subclasses implement only <see cref="TickAsync"/>.
/// </summary>
public abstract class PeriodicWorker(
    IServiceProvider services,
    ILogger logger,
    string name,
    TimeSpan startupDelay,
    TimeSpan interval) : BackgroundService
{
    protected IServiceProvider Services { get; } = services;
    protected ILogger Logger { get; } = logger;

    /// <summary>Delay before the next tick. Fixed by default; a tick may adjust it (e.g. from settings).</summary>
    protected TimeSpan Interval { get; set; } = interval;

    /// <summary>Delay after a failed tick. Defaults to <see cref="Interval"/>.</summary>
    protected virtual TimeSpan RetryDelay => Interval;

    /// <summary>One tick of work. Throwing is fine — it's logged and the loop continues.</summary>
    protected abstract Task TickAsync(CancellationToken ct);

    /// <summary>Runs once after the startup delay, before the first tick. Default: nothing.</summary>
    protected virtual Task OnStartupAsync(CancellationToken ct) => Task.CompletedTask;

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("{Worker} starting.", name);
        try { await Task.Delay(startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        try { await OnStartupAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Logger.LogError(ex, "{Worker} startup step failed.", name); }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = Interval;
            try { await TickAsync(stoppingToken); delay = Interval; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Worker} tick failed; will retry next interval.", name);
                delay = RetryDelay;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Resolves <typeparamref name="TService"/> in a fresh scope and runs <paramref name="action"/> against it.</summary>
    protected async Task WithScopedAsync<TService>(Func<TService, CancellationToken, Task> action, CancellationToken ct)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>(), ct);
    }

    /// <summary>
    /// Like <see cref="WithScopedAsync{TService}"/> but a failure is logged as a warning with
    /// <paramref name="failureMessage"/> and swallowed, so one optional sub-step can't abort the tick.
    /// </summary>
    protected async Task TryScopedAsync<TService>(
        Func<TService, CancellationToken, Task> action, string failureMessage, CancellationToken ct)
        where TService : notnull
    {
        try { await WithScopedAsync(action, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.LogWarning(ex, "{Message}", failureMessage); }
    }

    /// <summary>Idempotent universe seed — safe for every worker that depends on the universe to call at startup.</summary>
    protected Task SeedUniverseAsync(CancellationToken ct) =>
        WithScopedAsync<IStockUniverseSeeder>((s, c) => s.SeedAsync(c), ct);
}
