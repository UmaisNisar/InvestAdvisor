using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Trading;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Drives every short-horizon strategy independently of the fundamental <see cref="ScreenerWorker"/>:
/// each cycle it generates the day's setups (and resolves open paper trades) per strategy, and once a
/// day it re-runs each backtest gate. Bar fetches are HTTP and rate-limited, so this is intentionally
/// a slow loop. No LLM spend, so it isn't behind the budget guard.
/// </summary>
public sealed class StrategyWorker(IServiceProvider services, ILogger<StrategyWorker> logger, ISystemClock clock)
    : PeriodicWorker(services, logger, "Strategy worker", TimeSpan.FromSeconds(20), TimeSpan.FromHours(1))
{
    private static readonly TimeSpan BacktestCadence = TimeSpan.FromHours(24);
    private readonly Dictionary<StrategyKind, DateTime> _lastBacktestUtc = new();

    protected override Task OnStartupAsync(CancellationToken ct) => SeedUniverseAsync(ct);

    protected override async Task TickAsync(CancellationToken ct)
    {
        foreach (var kind in Enum.GetValues<StrategyKind>())
        {
            if (clock.UtcNow - _lastBacktestUtc.GetValueOrDefault(kind, DateTime.MinValue) >= BacktestCadence)
            {
                await WithScopedAsync<IStrategyService>((s, c) => s.RunBacktestAsync(kind, c), ct);
                _lastBacktestUtc[kind] = clock.UtcNow;
            }
            await WithScopedAsync<IStrategyService>((s, c) => s.GenerateSetupsAsync(kind, ct: c), ct);
        }
    }
}
