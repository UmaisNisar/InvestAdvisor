using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// In-process background worker: refresh market data, evaluate triggers, run the agent
/// when a trigger fires, dispatch notifications, and publish a <c>RunCompleted</c> event
/// so live Blazor pages can refresh.
/// </summary>
public sealed class InvestAdvisorWorker(
    IServiceProvider services,
    ILogger<InvestAdvisorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InvestAdvisor worker starting.");
        // Settle briefly so the host gets through Photino startup before we hit the network.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            int interval;
            try
            {
                interval = await RunOneTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in worker tick; will retry next interval.");
                interval = 60;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, interval)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<int> RunOneTickAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var dbFactory = sp.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        var settingsStore = sp.GetRequiredService<IRuntimeSettingsStore>();
        var clock = sp.GetRequiredService<ISystemClock>();
        var evaluator = sp.GetRequiredService<ITriggerEvaluator>();
        var priceRefresh = sp.GetRequiredService<IPriceRefreshService>();
        var newsRefresh = sp.GetRequiredService<INewsRefreshService>();
        var agent = sp.GetRequiredService<IAgentService>();
        var channels = sp.GetServices<INotificationChannel>().ToArray();
        var bus = sp.GetRequiredService<IRunEventBus>();

        var settings = await settingsStore.GetAsync(ct);
        var tickInterval = settings.TickIntervalSeconds;

        // Load the small singletons + tracked tickers.
        Profile profile;
        List<Holding> holdings;
        List<WatchlistItem> watchlist;
        DateTime? lastRun;
        int runsToday;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == Profile.SingletonId, ct);
            holdings = await db.Holdings.AsNoTracking().ToListAsync(ct);
            watchlist = await db.WatchlistItems.AsNoTracking().ToListAsync(ct);

            var nowUtc = clock.UtcNow;
            var dayStart = nowUtc.Date;
            lastRun = await db.AdviceLogs.AsNoTracking()
                .OrderByDescending(a => a.TimestampUtc)
                .Select(a => (DateTime?)a.TimestampUtc)
                .FirstOrDefaultAsync(ct);
            runsToday = await db.AdviceLogs.AsNoTracking()
                .CountAsync(a => a.TimestampUtc >= dayStart, ct);
        }

        var tickers = holdings.Select(h => new TickerSpec(h.Ticker, h.AssetClass))
            .Concat(watchlist.Select(w => new TickerSpec(w.Ticker, w.AssetClass)))
            .Distinct()
            .ToArray();

        if (tickers.Length > 0)
        {
            try { await priceRefresh.RefreshAsync(tickers, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Price refresh failed."); }

            try { await newsRefresh.RefreshAsync(tickers, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "News refresh failed."); }
        }

        // Reload latest snapshots so the evaluator sees what the refresh just wrote.
        Dictionary<string, PriceSnapshot> latestSnaps;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var tickerStrings = tickers.Select(t => t.Ticker).ToArray();
            var rows = await db.PriceSnapshots.AsNoTracking()
                .Where(s => tickerStrings.Contains(s.Ticker))
                .OrderByDescending(s => s.FetchedAtUtc)
                .ToListAsync(ct);
            latestSnaps = new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rows)
                if (!latestSnaps.ContainsKey(s.Ticker)) latestSnaps[s.Ticker] = s;
        }

        var trigger = evaluator.Evaluate(new EvaluationInput(
            NowUtc: clock.UtcNow,
            LastRunUtc: lastRun,
            RunsToday: runsToday,
            Profile: profile,
            Settings: settings,
            Holdings: holdings,
            Watchlist: watchlist,
            LatestSnapshotsByTicker: latestSnaps));

        if (trigger is null)
        {
            logger.LogDebug("No trigger this tick.");
            return tickInterval;
        }

        logger.LogInformation("Trigger fired: {Kind} — {Detail}", trigger.Kind, trigger.Detail);

        long adviceLogId;
        try
        {
            adviceLogId = await agent.RunAsync(trigger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent run threw an unhandled exception after persistence layer.");
            return tickInterval;
        }

        bus.Publish(new RunCompletedEvent(adviceLogId, clock.UtcNow, trigger.Kind.ToString()));

        await DispatchNotificationsAsync(sp, dbFactory, adviceLogId, channels, ct);

        return tickInterval;
    }

    private async Task DispatchNotificationsAsync(
        IServiceProvider sp,
        IDbContextFactory<InvestAdvisorDbContext> dbFactory,
        long adviceLogId,
        INotificationChannel[] channels,
        CancellationToken ct)
    {
        if (channels.Length == 0) return;

        AdviceLog row;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            row = await db.AdviceLogs.AsNoTracking().SingleAsync(a => a.Id == adviceLogId, ct);
        }

        var analysis = SerializeAnalysisBack(row);

        foreach (var ch in channels)
        {
            if (!ch.ShouldDispatch(analysis)) continue;

            AlertDelivery delivery;
            try
            {
                delivery = await ch.SendAsync(row, analysis, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification channel {Channel} failed for AdviceLog {Id}.",
                    ch.ChannelName, adviceLogId);
                delivery = new AlertDelivery
                {
                    AdviceLogId = adviceLogId,
                    Channel = ch.ChannelName,
                    Status = Core.Enums.DeliveryStatus.Failed,
                    ErrorMessage = ex.Message,
                    AttemptCount = 1,
                };
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AlertDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Rehydrates an <see cref="Core.Models.AgentAnalysis"/> from the persisted JSON columns.
    /// Channels only need the summary + structured arrays; metrics are best-effort.
    /// </summary>
    private static Core.Models.AgentAnalysis SerializeAnalysisBack(AdviceLog row)
    {
        var flags = System.Text.Json.JsonSerializer.Deserialize<Core.Models.Flag[]>(
            row.ParsedFlagsJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.CamelCase) },
            }) ?? Array.Empty<Core.Models.Flag>();
        var drift = System.Text.Json.JsonSerializer.Deserialize<Core.Models.DriftAlert[]>(
            row.ParsedDriftAlertsJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.CamelCase) },
            }) ?? Array.Empty<Core.Models.DriftAlert>();
        var cons = System.Text.Json.JsonSerializer.Deserialize<Core.Models.Consideration[]>(
            row.ParsedConsiderationsJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }) ?? Array.Empty<Core.Models.Consideration>();
        var positions = System.Text.Json.JsonSerializer.Deserialize<Core.Models.PositionCall[]>(
            row.ParsedPositionsJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
            }) ?? Array.Empty<Core.Models.PositionCall>();

        return new Core.Models.AgentAnalysis(
            Summary: row.ParsedSummary,
            Flags: flags,
            DriftAlerts: drift,
            Considerations: cons,
            Metrics: new Core.Models.AgentRunMetrics(
                Model: row.Model,
                InputTokens: row.InputTokens,
                OutputTokens: row.OutputTokens,
                LatencyMs: row.LatencyMs,
                ParseFallbackUsed: row.ParseFallbackUsed),
            Positions: positions);
    }
}
