using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Seeds the screener universe on startup, then refreshes fundamentals / analyst / insider data
/// about once a day. Runs independently of <see cref="InvestAdvisorWorker"/> so the multi-minute
/// batch fetch never delays price refreshes or trigger evaluation.
/// </summary>
public sealed class ScreenerWorker(
    IServiceProvider services,
    ILogger<ScreenerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncCadence = TimeSpan.FromHours(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Screener worker starting.");
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStockUniverseSeeder>().SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Universe seeding failed."); }

        // Sync FIRST when data is missing/stale, so the ranking and the (expensive) LLM analysis
        // always run on fresh data — never on an empty, zero-score universe. Then rank + analyse.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await IsSyncDueAsync(stoppingToken))
                {
                    await using var scope = services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IScreenerSyncService>().SyncAsync(stoppingToken);
                }

                await RankAndLogAsync(stoppingToken);
                await RunDailyRecommendationAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Screener tick failed; will retry next interval."); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RankAndLogAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var ranked = await scope.ServiceProvider.GetRequiredService<IScreenerScoringService>().RankAsync(ct: ct);
            if (ranked.Count == 0) return;

            var top = string.Join(", ", ranked.Take(8).Select(s => $"{s.Ticker} {s.CompositeScore:0.0}"));
            var bottom = string.Join(", ", ranked.TakeLast(5).Select(s => $"{s.Ticker} {s.CompositeScore:0.0}"));
            logger.LogInformation("Screener ranking ({Count} scored).\n  Top opportunities: {Top}\n  Top risks: {Bottom}",
                ranked.Count, top, bottom);

            // Persist today's snapshot once a day, capturing each stock's price so a
            // score-vs-forward-return validation can be computed as history accrues.
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
            var today = DateTime.UtcNow.Date;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (!await db.ScreenerScores.AnyAsync(s => s.AsOfDate == today, ct))
            {
                var market = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
                for (var i = 0; i < ranked.Count; i++)
                {
                    decimal? price = null;
                    try { price = (await market.GetQuoteAsync(ranked[i].Ticker, Core.Enums.AssetClass.Equity, ct))?.Price; }
                    catch (OperationCanceledException) { throw; }
                    catch { /* leave price null; validation just skips this name */ }

                    db.ScreenerScores.Add(new ScreenerScore
                    {
                        Ticker = ranked[i].Ticker,
                        AsOfDate = today,
                        CompositeScore = ranked[i].CompositeScore,
                        Rank = i + 1,
                        Price = price,
                    });
                }
                await db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Screener ranking failed."); }
    }

    private async Task RunDailyRecommendationAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
            var rec = sp.GetRequiredService<IDailyRecommendationService>();

            List<int> tenantIds;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

            foreach (var tenantId in tenantIds)
            {
                try
                {
                    var generated = await rec.GenerateAsync(tenantId, ct: ct);
                    if (generated) logger.LogInformation("Daily recommendation generated for tenant {Tenant}.", tenantId);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Daily recommendation failed for tenant {Tenant}.", tenantId); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Daily recommendation loop failed."); }
    }

    private async Task<bool> IsSyncDueAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var newest = await db.StockMetrics.AsNoTracking()
            .OrderByDescending(m => m.FetchedAtUtc)
            .Select(m => (DateTime?)m.FetchedAtUtc)
            .FirstOrDefaultAsync(ct);
        return newest is null || DateTime.UtcNow - newest.Value >= SyncCadence;
    }
}
