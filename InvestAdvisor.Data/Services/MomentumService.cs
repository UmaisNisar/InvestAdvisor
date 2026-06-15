using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Momentum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Drives the high-vol momentum module each scan cycle: rank the momentum universe and persist the
/// top qualifying breakout setups as today's candidate snapshot. Also replays the rule over history
/// for the backtest gate. Bars come from <see cref="IPriceHistoryProvider"/> (Yahoo — covers US +
/// TSX). Idempotent per UTC day unless forced. Mirrors <see cref="SwingService"/> but without
/// paper-trade resolution (the out-of-sample track record is a separate follow-up).
/// </summary>
public sealed class MomentumService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IPriceHistoryProvider history,
    IMomentumScoringService scoring,
    ISentimentScoringService sentiment,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock,
    ILogger<MomentumService>? logger = null) : IMomentumService
{
    public async Task<int> GenerateSetupsAsync(bool force = false, CancellationToken ct = default)
    {
        var today = clock.UtcNow.Date;
        var p = MomentumParams.For((await settingsStore.GetAsync(ct)).MomentumRiskLevel);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!force && await db.MomentumCandidates.AnyAsync(c => c.GeneratedAtUtc == today, ct))
        {
            logger?.LogInformation("Momentum candidates already generated for {Day:yyyy-MM-dd}; skipping.", today);
            return 0;
        }

        var universe = await LoadUniverseAsync(HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("Momentum universe is empty or no bars fetched."); return 0; }

        var sentimentByTicker = await TryGetSentimentAsync(ct);
        var ranked = scoring.Rank(universe, sentimentByTicker, p);
        var picks = ranked.Where(s => s.Qualifies).Take(p.SetupCount).ToList();

        // Replace today's snapshot wholesale so a re-scan reflects current levels.
        var stale = await db.MomentumCandidates.Where(c => c.GeneratedAtUtc == today).ToListAsync(ct);
        if (stale.Count > 0) db.MomentumCandidates.RemoveRange(stale);

        foreach (var s in picks)
            db.MomentumCandidates.Add(new MomentumCandidate
            {
                GeneratedAtUtc = today,
                Ticker = s.Ticker,
                Name = s.Name,
                EntryLow = s.Setup.EntryLow,
                EntryHigh = s.Setup.EntryHigh,
                EntryReference = s.Setup.EntryReference,
                StopLoss = s.Setup.StopLoss,
                Target = s.Setup.Target,
                RewardRiskRatio = s.Setup.RewardRiskRatio,
                HoldingDays = s.Setup.HoldingDays,
                PositionSizePct = s.Setup.PositionSizePct,
                TargetGainPct = Math.Round(s.Setup.TargetGainPct, 2),
                CompositeScore = s.CompositeScore,
                Kind = s.Setup.Kind,
                Rationale = s.Setup.Rationale,
                AtrPercent = s.Features.AtrPercent,
                BreakoutStrength = s.Features.BreakoutStrength,
                RelativeVolume = s.Features.RelativeVolume,
            });

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Logged {Count} momentum candidates for {Day:yyyy-MM-dd}: {Tickers}.",
            picks.Count, today, string.Join(", ", picks.Select(x => x.Ticker)));
        return picks.Count;
    }

    public async Task RunBacktestAsync(CancellationToken ct = default)
    {
        var p = MomentumParams.For((await settingsStore.GetAsync(ct)).MomentumRiskLevel);
        var universe = await LoadUniverseAsync(HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("Momentum backtest skipped — no universe bars."); return; }

        var summary = MomentumBacktester.Run(universe, p);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.MomentumBacktestResults.Add(new MomentumBacktestResult
        {
            GeneratedAtUtc = clock.UtcNow,
            TotalTrades = summary.TotalTrades,
            Wins = summary.Wins,
            Losses = summary.Losses,
            WinRatePct = summary.WinRatePct,
            AverageR = summary.AverageR,
            ExpectancyR = summary.ExpectancyR,
            ProfitFactor = summary.ProfitFactor,
            MaxDrawdownR = summary.MaxDrawdownR,
            AverageHoldingDays = summary.AverageHoldingDays,
            FromUtc = summary.FromUtc,
            ToUtc = summary.ToUtc,
        });
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Momentum backtest: {Trades} trades, {Win:0.0}% win, {Exp:0.000}R expectancy, PF {Pf:0.00}.",
            summary.TotalTrades, summary.WinRatePct, summary.ExpectancyR, summary.ProfitFactor);
    }

    private async Task<IReadOnlyList<MomentumInput>> LoadUniverseAsync(HistoryRange range, CancellationToken ct)
    {
        List<Stock> stocks;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            stocks = await db.Stocks.AsNoTracking()
                .Where(s => s.IsActive && s.IsMomentumUniverse)
                .ToListAsync(ct);

        var inputs = new List<MomentumInput>(stocks.Count);
        foreach (var s in stocks)
        {
            var hist = await SafeHistoryAsync(s.Ticker, range, ct, s.AssetClass);
            if (hist is { Candles.Count: > 0 })
                inputs.Add(new MomentumInput(s.Ticker, s.Name, s.Sector, s.AssetClass, hist.Candles));
        }
        return inputs;
    }

    private async Task<PriceHistory?> SafeHistoryAsync(string ticker, HistoryRange range, CancellationToken ct, AssetClass assetClass = AssetClass.Equity)
    {
        try { return await history.GetHistoryAsync(ticker, assetClass, range, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "Momentum history fetch failed for {Ticker}.", ticker); return null; }
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> TryGetSentimentAsync(CancellationToken ct)
    {
        try
        {
            var map = await sentiment.GetTickerSentimentAsync(ct);
            return map.ToDictionary(kv => kv.Key, kv => kv.Value.MeanScore, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "Momentum sentiment lookup failed; scoring without it."); return null; }
    }
}
