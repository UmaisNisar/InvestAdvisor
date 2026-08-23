using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Trading;
using InvestAdvisor.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

/// <summary>
/// Read models for a strategy page. Today's setups are the most recently generated open paper
/// trades of that strategy; the track record is computed from its resolved ones; the gate comes
/// from its latest backtest.
/// </summary>
public sealed class StrategyQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore) : IStrategyQueries
{
    public async Task<StrategyDashboard> GetDashboardAsync(StrategyKind kind, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var settings = await settingsStore.GetAsync(ct);
        var riskLevel = kind == StrategyKind.Swing ? settings.SwingRiskLevel : settings.MomentumRiskLevel;
        var universeSize = await db.Stocks.AsNoTracking()
            .CountAsync(s => s.IsActive && (kind == StrategyKind.Swing ? s.IsSwingUniverse : s.IsMomentumUniverse), ct);

        var trades = db.PaperTrades.AsNoTracking().Where(t => t.Strategy == kind);

        // Today's setups = the open trades from the latest generation date.
        var latestDate = await trades
            .Where(t => t.Status == PaperTradeStatus.Open)
            .OrderByDescending(t => t.GeneratedAtUtc)
            .Select(t => (DateTime?)t.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        var setups = latestDate is null
            ? new List<SetupView>()
            : (await trades
                    .Where(t => t.Status == PaperTradeStatus.Open && t.GeneratedAtUtc == latestDate)
                    .OrderByDescending(t => t.CompositeScore)
                    .ToListAsync(ct))
                .Select(t => new SetupView(
                    t.Ticker, t.Name, t.EntryLow, t.EntryHigh, t.StopLoss, t.Target,
                    t.RewardRiskRatio, t.HoldingDays, t.PositionSizePct, t.TargetGainPct, t.CompositeScore,
                    t.Kind, t.Rationale, t.GeneratedAtUtc))
                .ToList();

        var openCount = await trades.CountAsync(t => t.Status == PaperTradeStatus.Open, ct);
        var resolved = await trades
            .Where(t => t.Status != PaperTradeStatus.Open && t.RealizedR != null)
            .Select(t => t.RealizedR!.Value)
            .ToListAsync(ct);

        TrackRecordView? track = null;
        if (resolved.Count > 0 || openCount > 0)
        {
            var wins = resolved.Count(r => r > 0m);
            track = new TrackRecordView(
                Resolved: resolved.Count,
                Open: openCount,
                Wins: wins,
                Losses: resolved.Count - wins,
                WinRatePct: resolved.Count == 0 ? 0m : Math.Round((decimal)wins / resolved.Count * 100m, 1),
                TotalR: Math.Round(resolved.Sum(), 2),
                AverageR: resolved.Count == 0 ? 0m : Math.Round(resolved.Average(), 3));
        }

        // Watchlist = the latest near-setups snapshot (closest-to-triggering names).
        var watchItems = db.SwingWatchItems.AsNoTracking().Where(w => w.Strategy == kind);
        var watchDate = await watchItems
            .OrderByDescending(w => w.GeneratedAtUtc)
            .Select(w => (DateTime?)w.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);
        var watchlist = watchDate is null
            ? new List<WatchView>()
            : (await watchItems
                    .Where(w => w.GeneratedAtUtc == watchDate)
                    .OrderBy(w => w.Rsi)
                    .ToListAsync(ct))
                .Select(w => new WatchView(w.Ticker, w.Name, w.Close, w.Rsi, w.RegimeDistancePct, w.Note))
                .ToList();

        var bt = await db.BacktestResults.AsNoTracking()
            .Where(x => x.Strategy == kind)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        BacktestView? backtest = null;
        var validated = false;
        if (bt is not null)
        {
            var minProfitFactor = StrategyService.StrategyFor(kind).ParamsFor(riskLevel).MinProfitFactor;
            validated = bt.ToSummary().HasEdge(minProfitFactor);
            backtest = new BacktestView(
                bt.GeneratedAtUtc, bt.TotalTrades, bt.WinRatePct, bt.ExpectancyR, bt.ProfitFactor,
                bt.MaxDrawdownR, bt.AverageHoldingDays, bt.FromUtc, bt.ToUtc, validated);
        }

        return new StrategyDashboard(kind, universeSize, riskLevel, latestDate, validated, setups, watchlist, track, backtest);
    }
}
