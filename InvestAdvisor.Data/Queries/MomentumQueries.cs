using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Momentum;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

/// <summary>
/// Read model for the Momentum page. Today's setups are the latest candidate snapshot; the gate comes
/// from the latest backtest. Mirrors <see cref="SwingQueries"/> minus the paper-trade track record.
/// </summary>
public sealed class MomentumQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore) : IMomentumQueries
{
    public async Task<MomentumDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var riskLevel = (await settingsStore.GetAsync(ct)).MomentumRiskLevel;
        var universeSize = await db.Stocks.AsNoTracking().CountAsync(s => s.IsActive && s.IsMomentumUniverse, ct);

        var latestDate = await db.MomentumCandidates.AsNoTracking()
            .OrderByDescending(c => c.GeneratedAtUtc)
            .Select(c => (DateTime?)c.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        var setups = latestDate is null
            ? new List<MomentumSetupView>()
            : (await db.MomentumCandidates.AsNoTracking()
                    .Where(c => c.GeneratedAtUtc == latestDate)
                    .OrderByDescending(c => c.CompositeScore)
                    .ToListAsync(ct))
                .Select(c => new MomentumSetupView(
                    c.Ticker, c.Name, c.EntryLow, c.EntryHigh, c.StopLoss, c.Target,
                    c.RewardRiskRatio, c.HoldingDays, c.PositionSizePct, c.TargetGainPct, c.CompositeScore,
                    c.Kind, c.Rationale, c.AtrPercent, c.BreakoutStrength, c.RelativeVolume, c.GeneratedAtUtc))
                .ToList();

        var bt = await db.MomentumBacktestResults.AsNoTracking()
            .OrderByDescending(x => x.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        MomentumBacktestView? backtest = null;
        var validated = false;
        if (bt is not null)
        {
            var summary = new MomentumBacktestSummary(bt.TotalTrades, bt.Wins, bt.Losses, bt.WinRatePct,
                bt.AverageR, bt.ExpectancyR, bt.ProfitFactor, bt.MaxDrawdownR, bt.AverageHoldingDays,
                bt.FromUtc, bt.ToUtc);
            validated = summary.HasEdge();
            backtest = new MomentumBacktestView(
                bt.GeneratedAtUtc, bt.TotalTrades, bt.WinRatePct, bt.ExpectancyR, bt.ProfitFactor,
                bt.MaxDrawdownR, bt.AverageHoldingDays, bt.FromUtc, bt.ToUtc, validated);
        }

        return new MomentumDashboard(universeSize, riskLevel, latestDate, validated, setups, backtest);
    }
}
