using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Drives the short-horizon swing module independently of the fundamental <see cref="ScreenerWorker"/>:
/// each cycle it generates the day's setups (and resolves open paper trades), and once a day it
/// re-runs the backtest gate. Bar fetches are HTTP and rate-limited, so this is intentionally a slow
/// loop. No LLM spend, so unlike the daily recommendation it isn't behind the budget guard.
/// </summary>
public sealed class SwingWorker(
    IServiceProvider services,
    ILogger<SwingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BacktestCadence = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Swing worker starting.");
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Ensure the swing universe exists even if the screener worker is disabled (idempotent).
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStockUniverseSeeder>().SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Swing universe seeding failed."); }

        DateTime lastBacktestUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastBacktestUtc >= BacktestCadence)
                {
                    await using var scope = services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ISwingService>().RunBacktestAsync(stoppingToken);
                    lastBacktestUtc = DateTime.UtcNow;
                }

                await using (var scope = services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<ISwingService>().GenerateSetupsAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Swing tick failed; will retry next interval."); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
