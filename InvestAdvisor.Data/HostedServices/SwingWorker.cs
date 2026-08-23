using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Drives the short-horizon swing module independently of the fundamental <see cref="ScreenerWorker"/>:
/// each cycle it generates the day's setups (and resolves open paper trades), and once a day it
/// re-runs the backtest gate. Bar fetches are HTTP and rate-limited, so this is intentionally a slow
/// loop. No LLM spend, so unlike the daily recommendation it isn't behind the budget guard.
/// </summary>
public sealed class SwingWorker(IServiceProvider services, ILogger<SwingWorker> logger, ISystemClock clock)
    : PeriodicWorker(services, logger, "Swing worker", TimeSpan.FromSeconds(20), TimeSpan.FromHours(1))
{
    private static readonly TimeSpan BacktestCadence = TimeSpan.FromHours(24);
    private DateTime _lastBacktestUtc = DateTime.MinValue;

    protected override Task OnStartupAsync(CancellationToken ct) => SeedUniverseAsync(ct);

    protected override async Task TickAsync(CancellationToken ct)
    {
        if (clock.UtcNow - _lastBacktestUtc >= BacktestCadence)
        {
            await WithScopedAsync<ISwingService>((s, c) => s.RunBacktestAsync(c), ct);
            _lastBacktestUtc = clock.UtcNow;
        }
        await WithScopedAsync<ISwingService>((s, c) => s.GenerateSetupsAsync(ct: c), ct);
    }
}
