using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Drives the high-vol momentum module: each cycle it generates the day's breakout candidates, and
/// once a day it re-runs the backtest gate. Same shape as <see cref="SwingWorker"/>; no LLM spend.
/// </summary>
public sealed class MomentumWorker(IServiceProvider services, ILogger<MomentumWorker> logger, ISystemClock clock)
    : PeriodicWorker(services, logger, "Momentum worker", TimeSpan.FromSeconds(30), TimeSpan.FromHours(1))
{
    private static readonly TimeSpan BacktestCadence = TimeSpan.FromHours(24);
    private DateTime _lastBacktestUtc = DateTime.MinValue;

    protected override Task OnStartupAsync(CancellationToken ct) => SeedUniverseAsync(ct);

    protected override async Task TickAsync(CancellationToken ct)
    {
        if (clock.UtcNow - _lastBacktestUtc >= BacktestCadence)
        {
            await WithScopedAsync<IMomentumService>((s, c) => s.RunBacktestAsync(c), ct);
            _lastBacktestUtc = clock.UtcNow;
        }
        await WithScopedAsync<IMomentumService>((s, c) => s.GenerateSetupsAsync(ct: c), ct);
    }
}
