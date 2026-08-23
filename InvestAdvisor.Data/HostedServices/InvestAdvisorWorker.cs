using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// In-process background worker: refresh market data, evaluate triggers, run the agent
/// when a trigger fires, dispatch notifications, and publish a <c>RunCompleted</c> event
/// so live Blazor pages can refresh.
/// </summary>
public sealed class InvestAdvisorWorker(IServiceProvider services, ILogger<InvestAdvisorWorker> logger)
    : PeriodicWorker(services, logger, "InvestAdvisor worker", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60))
{
    // Tick cadence comes from RuntimeSettings; floor it so a misconfigured value can't hot-loop.
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);
    protected override TimeSpan RetryDelay => TimeSpan.FromSeconds(60);

    // Per-tenant dedup keys of condition triggers already alerted on and not yet re-armed. Held in
    // process memory (not the DB) — a restart re-arms everything, costing at most one extra run per
    // still-breached condition, which is fine for intraday spam suppression.
    private static readonly IReadOnlySet<string> EmptyKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, IReadOnlySet<string>> _suppressedKeysByTenant = new();

    protected override async Task TickAsync(CancellationToken ct)
    {
        var seconds = await RunOneTickAsync(ct);
        Interval = TimeSpan.FromSeconds(Math.Max(MinInterval.TotalSeconds, seconds));
    }

    private async Task<int> RunOneTickAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
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

        // --- Shared market-data refresh: the union of ALL tenants' tickers (one fetch each). ---
        List<TickerSpec> allTickers;
        List<Tenant> tenants;
        List<string> holdingCurrencies;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var hs = await db.Holdings.AsNoTracking().Select(h => new TickerSpec(h.Ticker, h.AssetClass)).ToListAsync(ct);
            var ws = await db.WatchlistItems.AsNoTracking().Select(w => new TickerSpec(w.Ticker, w.AssetClass)).ToListAsync(ct);
            allTickers = hs.Concat(ws).Distinct().ToList();
            tenants = await db.Tenants.AsNoTracking().ToListAsync(ct);
            holdingCurrencies = await db.Holdings.AsNoTracking().Select(h => h.Currency).Distinct().ToListAsync(ct);
        }

        // FX rates for drift evaluation: allocation percentages need every holding in USD.
        var fxRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        var fx = sp.GetRequiredService<IFxRateProvider>();
        foreach (var c in holdingCurrencies
                     .Select(c => string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (fxRates.ContainsKey(c)) continue;
            try { fxRates[c] = await fx.GetRateToUsdAsync(c, ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "FX rate fetch failed for {Currency}; using 1.0 for drift math.", c); }
        }

        if (allTickers.Count > 0)
        {
            try
            {
                await priceRefresh.RefreshAsync(allTickers.ToArray(), ct);
                bus.Publish(new PricesRefreshedEvent(allTickers.Select(t => t.Ticker).ToArray(), clock.UtcNow));
            }
            catch (Exception ex) { Logger.LogWarning(ex, "Price refresh failed."); }

            try { await newsRefresh.RefreshAsync(allTickers.ToArray(), ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "News refresh failed."); }
        }

        // Reload latest snapshots (shared across tenants) so each tenant's evaluator sees fresh prices.
        Dictionary<string, PriceSnapshot> latestSnaps;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var tickerStrings = allTickers.Select(t => t.Ticker).ToArray();
            var rows = await db.PriceSnapshots.AsNoTracking()
                .Where(s => tickerStrings.Contains(s.Ticker))
                .OrderByDescending(s => s.FetchedAtUtc)
                .ToListAsync(ct);
            latestSnaps = new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rows)
                if (!latestSnaps.ContainsKey(s.Ticker)) latestSnaps[s.Ticker] = s;
        }

        // Cost guards: a manual pause or the daily budget short-circuits the LLM-spending part of the
        // tick. Price/news refresh above still ran, so the dashboard stays fresh while runs are held.
        if (await sp.GetRequiredService<ICostService>().GetSpendHoldReasonAsync(ct) is { } hold)
        {
            Logger.LogInformation("{Reason}; skipping trigger evaluation and runs this tick.", hold);
            return tickInterval;
        }

        // --- Per-tenant: evaluate triggers against that tenant's portfolio, run the agent if fired. ---
        foreach (var tenant in tenants)
        {
            Profile? profile;
            List<Holding> holdings;
            List<WatchlistItem> watchlist;
            DateTime? lastRun;
            int runsToday;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenant.Id, ct);
                if (profile is null) continue; // tenant not provisioned yet
                holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tenant.Id).ToListAsync(ct);
                watchlist = await db.WatchlistItems.AsNoTracking().Where(w => w.TenantId == tenant.Id).ToListAsync(ct);
                var dayStart = clock.UtcNow.Date;
                lastRun = await db.AdviceLogs.AsNoTracking()
                    .Where(a => a.TenantId == tenant.Id)
                    .OrderByDescending(a => a.TimestampUtc)
                    .Select(a => (DateTime?)a.TimestampUtc).FirstOrDefaultAsync(ct);
                runsToday = await db.AdviceLogs.AsNoTracking()
                    .CountAsync(a => a.TenantId == tenant.Id && a.TimestampUtc >= dayStart, ct);
            }

            var suppressed = _suppressedKeysByTenant.GetValueOrDefault(tenant.Id, EmptyKeys);
            var decision = evaluator.Evaluate(new EvaluationInput(
                NowUtc: clock.UtcNow,
                LastRunUtc: lastRun,
                RunsToday: runsToday,
                Profile: profile,
                Settings: settings,
                Holdings: holdings,
                Watchlist: watchlist,
                LatestSnapshotsByTicker: latestSnaps,
                SuppressedKeys: suppressed,
                FxRatesToUsd: fxRates));

            // Carry the re-armed/alerted dedup set into this tenant's next tick so a persistent
            // condition (e.g. a stock down -15% all session) fires once, not every tick.
            _suppressedKeysByTenant[tenant.Id] = decision.ActiveKeys;

            var trigger = decision.Trigger;
            if (trigger is null) continue;

            Logger.LogInformation("Trigger fired for tenant {Tenant}: {Kind} — {Detail}", tenant.Id, trigger.Kind, trigger.Detail);

            long adviceLogId;
            try
            {
                adviceLogId = await agent.RunAsync(tenant.Id, trigger, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Agent run threw for tenant {Tenant}.", tenant.Id);
                continue;
            }

            bus.Publish(new RunCompletedEvent(adviceLogId, clock.UtcNow, trigger.Kind.ToString()));
            await DispatchNotificationsAsync(sp, dbFactory, adviceLogId, channels, ct);
        }

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

            try
            {
                var outcome = await ch.SendAsync(row, analysis, ct);
                if (outcome.Status == Core.Enums.DeliveryStatus.Sent)
                    Logger.LogInformation("Notification channel {Channel} delivered AdviceLog {Id}.", ch.ChannelName, adviceLogId);
                else
                    Logger.LogWarning("Notification channel {Channel} {Status} for AdviceLog {Id}: {Error}",
                        ch.ChannelName, outcome.Status, adviceLogId, outcome.ErrorMessage);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Notification channel {Channel} failed for AdviceLog {Id}.",
                    ch.ChannelName, adviceLogId);
            }
        }
    }

    /// <summary>
    /// Rehydrates an <see cref="Core.Models.AgentAnalysis"/> from the persisted JSON columns.
    /// Channels only need the summary + structured arrays; metrics are best-effort.
    /// </summary>
    private static Core.Models.AgentAnalysis SerializeAnalysisBack(AdviceLog row)
    {
        return new Core.Models.AgentAnalysis(
            Summary: row.ParsedSummary,
            Flags: Core.Agent.JsonOptions.ArrayOrEmpty<Core.Models.Flag>(row.ParsedFlagsJson),
            DriftAlerts: Core.Agent.JsonOptions.ArrayOrEmpty<Core.Models.DriftAlert>(row.ParsedDriftAlertsJson),
            Considerations: Core.Agent.JsonOptions.ArrayOrEmpty<Core.Models.Consideration>(row.ParsedConsiderationsJson),
            Metrics: new Core.Models.AgentRunMetrics(
                Model: row.Model,
                InputTokens: row.InputTokens,
                OutputTokens: row.OutputTokens,
                LatencyMs: row.LatencyMs,
                ParseFallbackUsed: row.ParseFallbackUsed),
            Positions: Core.Agent.JsonOptions.ArrayOrEmpty<Core.Models.PositionCall>(row.ParsedPositionsJson));
    }
}
