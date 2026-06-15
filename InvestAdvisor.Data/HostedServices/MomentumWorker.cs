using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Drives the high-vol momentum module independently of the other workers: each cycle it generates
/// the day's breakout candidates, and once a day it re-runs the backtest gate. Bar fetches are HTTP
/// and rate-limited, so this is intentionally a slow loop. No LLM spend, so it isn't behind the budget
/// guard. Mirrors <see cref="SwingWorker"/>.
/// </summary>
public sealed class MomentumWorker(
    IServiceProvider services,
    ILogger<MomentumWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BacktestCadence = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Momentum worker starting.");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Ensure the momentum universe exists even if the screener/swing workers are disabled (idempotent).
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStockUniverseSeeder>().SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Momentum universe seeding failed."); }

        DateTime lastBacktestUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastBacktestUtc >= BacktestCadence)
                {
                    await using var scope = services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMomentumService>().RunBacktestAsync(stoppingToken);
                    lastBacktestUtc = DateTime.UtcNow;
                }

                await using (var scope = services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<IMomentumService>().GenerateSetupsAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Momentum tick failed; will retry next interval."); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
