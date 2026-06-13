using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Swing;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

/// <summary>
/// Read models for the Swing page. Today's setups are the most recently generated open paper
/// trades; the track record is computed from resolved ones; the gate comes from the latest backtest.
/// </summary>
public sealed class SwingQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore) : ISwingQueries
{
    public async Task<SwingDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var riskLevel = (await settingsStore.GetAsync(ct)).SwingRiskLevel;
        var universeSize = await db.Stocks.AsNoTracking().CountAsync(s => s.IsActive && s.IsSwingUniverse, ct);

        // Today's setups = the open trades from the latest generation date.
        var latestDate = await db.PaperTrades.AsNoTracking()
            .Where(t => t.Status == PaperTradeStatus.Open)
            .OrderByDescending(t => t.GeneratedAtUtc)
            .Select(t => (DateTime?)t.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        var setups = latestDate is null
            ? new List<SwingSetupView>()
            : (await db.PaperTrades.AsNoTracking()
                    .Where(t => t.Status == PaperTradeStatus.Open && t.GeneratedAtUtc == latestDate)
                    .OrderByDescending(t => t.CompositeScore)
                    .ToListAsync(ct))
                .Select(t => new SwingSetupView(
                    t.Ticker, t.Name, t.EntryLow, t.EntryHigh, t.StopLoss, t.Target,
                    t.RewardRiskRatio, t.HoldingDays, t.PositionSizePct, t.CompositeScore,
                    t.Rationale, t.GeneratedAtUtc))
                .ToList();

        var openCount = await db.PaperTrades.AsNoTracking().CountAsync(t => t.Status == PaperTradeStatus.Open, ct);
        var resolved = await db.PaperTrades.AsNoTracking()
            .Where(t => t.Status != PaperTradeStatus.Open && t.RealizedR != null)
            .Select(t => t.RealizedR!.Value)
            .ToListAsync(ct);

        SwingTrackRecord? track = null;
        if (resolved.Count > 0 || openCount > 0)
        {
            var wins = resolved.Count(r => r > 0m);
            var losses = resolved.Count(r => r <= 0m);
            track = new SwingTrackRecord(
                Resolved: resolved.Count,
                Open: openCount,
                Wins: wins,
                Losses: losses,
                WinRatePct: resolved.Count == 0 ? 0m : Math.Round((decimal)wins / resolved.Count * 100m, 1),
                TotalR: Math.Round(resolved.Sum(), 2),
                AverageR: resolved.Count == 0 ? 0m : Math.Round(resolved.Average(), 3));
        }

        // Watchlist = the latest near-setups snapshot (closest-to-triggering names).
        var watchDate = await db.SwingWatchItems.AsNoTracking()
            .OrderByDescending(w => w.GeneratedAtUtc)
            .Select(w => (DateTime?)w.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);
        var watchlist = watchDate is null
            ? new List<SwingWatchView>()
            : (await db.SwingWatchItems.AsNoTracking()
                    .Where(w => w.GeneratedAtUtc == watchDate)
                    .OrderBy(w => w.Rsi)
                    .ToListAsync(ct))
                .Select(w => new SwingWatchView(w.Ticker, w.Name, w.Close, w.Rsi, w.RegimeDistancePct, w.Note))
                .ToList();

        var bt = await db.SwingBacktestResults.AsNoTracking()
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        SwingBacktestView? backtest = null;
        var validated = false;
        if (bt is not null)
        {
            var summary = new SwingBacktestSummary(bt.TotalTrades, bt.Wins, bt.Losses, bt.WinRatePct,
                bt.AverageR, bt.ExpectancyR, bt.ProfitFactor, bt.MaxDrawdownR, bt.AverageHoldingDays,
                bt.FromUtc, bt.ToUtc);
            validated = summary.HasEdge();
            backtest = new SwingBacktestView(
                bt.GeneratedAtUtc, bt.TotalTrades, bt.WinRatePct, bt.ExpectancyR, bt.ProfitFactor,
                bt.MaxDrawdownR, bt.AverageHoldingDays, bt.FromUtc, bt.ToUtc, validated);
        }

        return new SwingDashboard(universeSize, riskLevel, latestDate, validated, setups, watchlist, track, backtest);
    }
}
