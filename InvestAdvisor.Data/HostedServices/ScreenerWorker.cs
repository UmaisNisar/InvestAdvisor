using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Seeds the screener universe on startup, then refreshes fundamentals / analyst / insider data
/// about once a day. Runs independently of <see cref="InvestAdvisorWorker"/> so the multi-minute
/// batch fetch never delays price refreshes or trigger evaluation.
/// </summary>
public sealed class ScreenerWorker(IServiceProvider services, ILogger<ScreenerWorker> logger, ISystemClock clock)
    : PeriodicWorker(services, logger, "Screener worker", TimeSpan.FromSeconds(10), TimeSpan.FromHours(1))
{
    private static readonly TimeSpan SyncCadence = TimeSpan.FromHours(20);

    protected override Task OnStartupAsync(CancellationToken ct) => SeedUniverseAsync(ct);

    // Sync FIRST when data is missing/stale, so the ranking and the (expensive) LLM call always
    // run on fresh data — never on an empty, zero-score universe. Then rank + recommend.
    protected override async Task TickAsync(CancellationToken ct)
    {
        if (await IsSyncDueAsync(ct))
            await WithScopedAsync<IScreenerSyncService>((s, c) => s.SyncAsync(c), ct);

        await TryScopedAsync<ISocialRefreshService>((s, c) => s.RefreshAsync(c),
            "Social refresh failed; continuing with existing posts.", ct);
        await TryScopedAsync<ISentimentScoringService>((s, c) => s.ScoreUnscoredAsync(c),
            "Sentiment scoring failed; ranking will use prior scores.", ct);
        await RankAndLogAsync(ct);
        await RunDailyRecommendationAsync(ct);
    }

    private async Task RankAndLogAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var ranked = await scope.ServiceProvider.GetRequiredService<IScreenerScoringService>().RankAsync(ct: ct);
            if (ranked.Count == 0) return;

            var top = string.Join(", ", ranked.Take(8).Select(s => $"{s.Ticker} {s.CompositeScore:0.0}"));
            var bottom = string.Join(", ", ranked.TakeLast(5).Select(s => $"{s.Ticker} {s.CompositeScore:0.0}"));
            Logger.LogInformation("Screener ranking ({Count} scored).\n  Top opportunities: {Top}\n  Top risks: {Bottom}",
                ranked.Count, top, bottom);

            // Persist today's snapshot once a day, capturing each stock's price so a
            // score-vs-forward-return validation can be computed as history accrues.
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
            var today = clock.UtcNow.Date;
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
        catch (Exception ex) { Logger.LogWarning(ex, "Screener ranking failed."); }
    }

    private async Task RunDailyRecommendationAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
            var rec = sp.GetRequiredService<IDailyRecommendationService>();

            // Same cost guards as the agent loop: pause and the daily budget hold off the (LLM) daily rec.
            if (await sp.GetRequiredService<ICostService>().GetSpendHoldReasonAsync(ct) is { } hold)
            {
                Logger.LogInformation("{Reason}; skipping daily recommendation.", hold);
                return;
            }

            List<int> tenantIds;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

            foreach (var tenantId in tenantIds)
            {
                try
                {
                    var generated = await rec.GenerateAsync(tenantId, ct: ct);
                    if (generated) Logger.LogInformation("Daily recommendation generated for tenant {Tenant}.", tenantId);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logger.LogWarning(ex, "Daily recommendation failed for tenant {Tenant}.", tenantId); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.LogWarning(ex, "Daily recommendation loop failed."); }
    }

    private async Task<bool> IsSyncDueAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var newest = await db.StockMetrics.AsNoTracking()
            .OrderByDescending(m => m.FetchedAtUtc)
            .Select(m => (DateTime?)m.FetchedAtUtc)
            .FirstOrDefaultAsync(ct);
        return newest is null || clock.UtcNow - newest.Value >= SyncCadence;
    }
}
