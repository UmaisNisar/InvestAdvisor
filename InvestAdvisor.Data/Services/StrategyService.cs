using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Drives every short-horizon strategy each scan cycle: resolve open paper trades from fresh bars,
/// then rank the strategy's universe and log the top qualifying setups as new paper trades (plus
/// the near-setup watchlist). Also runs the backtest gate over two years of history. Bars come from
/// <see cref="IPriceHistoryProvider"/> (Yahoo — covers both US and TSX). Idempotent per UTC day so
/// re-ticks don't double-log.
/// </summary>
public sealed class StrategyService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IPriceHistoryProvider history,
    ISentimentScoringService sentiment,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock,
    ILogger<StrategyService>? logger = null) : IStrategyService
{
    /// <summary>How many near-setups to keep on the watchlist when no/few setups qualify.</summary>
    private const int WatchlistCount = 8;

    public static IStrategy StrategyFor(StrategyKind kind) => kind switch
    {
        StrategyKind.Swing => SwingStrategy.Instance,
        StrategyKind.Momentum => MomentumStrategy.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public async Task<int> GenerateSetupsAsync(StrategyKind kind, bool force = false, CancellationToken ct = default)
    {
        var strategy = StrategyFor(kind);
        var today = clock.UtcNow.Date;
        var p = await ParamsAsync(strategy, ct);

        await ResolveOpenTradesAsync(kind, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!force && await db.PaperTrades.AnyAsync(t => t.Strategy == kind && t.GeneratedAtUtc == today, ct))
        {
            logger?.LogInformation("{Strategy} setups already generated for {Day:yyyy-MM-dd}; skipping.", kind, today);
            return 0;
        }

        // A manual re-scan refreshes today's snapshot: drop today's still-open setups so they're
        // regenerated at current levels. Resolved trades (the track record) are never touched.
        if (force)
        {
            var stale = await db.PaperTrades
                .Where(t => t.Strategy == kind && t.GeneratedAtUtc == today && t.Status == PaperTradeStatus.Open)
                .ToListAsync(ct);
            if (stale.Count > 0) { db.PaperTrades.RemoveRange(stale); await db.SaveChangesAsync(ct); }
        }

        // Two years of daily bars: the swing regime filter needs a full 200-day SMA.
        var universe = await LoadUniverseAsync(kind, HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("{Strategy} universe is empty or no bars fetched.", kind); return 0; }

        var ranked = StrategyScorer.Rank(strategy, universe, p, await TryGetSentimentAsync(kind, ct));

        // Always refresh the watchlist (near-setups), even when nothing qualifies — so the page is
        // never blank. Replace today's snapshot.
        await RefreshWatchlistAsync(db, strategy, ranked, p, today, ct);

        var picks = ranked.Where(s => s.Qualifies).Take(p.SetupCount).ToList();
        if (picks.Count == 0) { logger?.LogInformation("No qualifying {Strategy} setups today.", kind); return 0; }

        var added = 0;
        foreach (var s in picks)
        {
            // Unique (Strategy, Ticker, GeneratedAtUtc=today) guards against a same-day re-run racing in.
            if (await db.PaperTrades.AnyAsync(t => t.Strategy == kind && t.Ticker == s.Ticker && t.GeneratedAtUtc == today, ct)) continue;
            db.PaperTrades.Add(ToPaperTrade(kind, s, today));
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        logger?.LogInformation("Logged {Count} {Strategy} setups for {Day:yyyy-MM-dd}: {Tickers}.",
            added, kind, today, string.Join(", ", picks.Select(x => x.Ticker)));
        return added;
    }

    private static PaperTrade ToPaperTrade(StrategyKind kind, StrategyScore s, DateTime today) => new()
    {
        Strategy = kind,
        Ticker = s.Ticker,
        Name = s.Name,
        GeneratedAtUtc = today,
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
        Rationale = s.Setup.Rationale,
        Kind = s.Setup.Kind,
        Status = PaperTradeStatus.Open,
        // Signal context at entry — so each resolved trade is a labelled example of what
        // conditions did/didn't pay off (the outcome-tracking dataset).
        SignalRsi = s.Features.Rsi,
        RelativeVolume = s.Features.RelativeVolume,
        RegimeDistancePct = (s.Features as SwingFeatures)?.RegimeDistancePct,
        PullbackPct = (s.Features as SwingFeatures)?.PullbackPct,
        AtrPercent = (s.Features as MomentumFeatures)?.AtrPercent,
        BreakoutStrength = (s.Features as MomentumFeatures)?.BreakoutStrength,
    };

    /// <summary>Replaces today's watchlist for the strategy with its near-setups, closest first.</summary>
    private static async Task RefreshWatchlistAsync(
        InvestAdvisorDbContext db, IStrategy strategy, IReadOnlyList<StrategyScore> ranked, StrategyParams p,
        DateTime today, CancellationToken ct)
    {
        var existing = await db.SwingWatchItems.Where(w => w.Strategy == strategy.Kind && w.GeneratedAtUtc == today).ToListAsync(ct);
        if (existing.Count > 0) db.SwingWatchItems.RemoveRange(existing);

        foreach (var (s, note) in strategy.Watch(ranked, p).Take(WatchlistCount))
            db.SwingWatchItems.Add(new SwingWatchItem
            {
                Strategy = strategy.Kind,
                GeneratedAtUtc = today,
                Ticker = s.Ticker,
                Name = s.Name,
                Close = s.Features.Close,
                CompositeScore = s.CompositeScore,
                Rsi = s.Features.Rsi,
                RegimeDistancePct = (s.Features as SwingFeatures)?.RegimeDistancePct,
                TrendDistancePct = s.Features.TrendDistancePct,
                Note = note,
            });

        await db.SaveChangesAsync(ct);
    }

    public async Task RunBacktestAsync(StrategyKind kind, CancellationToken ct = default)
    {
        var strategy = StrategyFor(kind);
        var p = await ParamsAsync(strategy, ct);
        var universe = await LoadUniverseAsync(kind, HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("{Strategy} backtest skipped — no universe bars.", kind); return; }

        var summary = Backtester.Run(strategy, universe, p);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.BacktestResults.Add(BacktestResult.From(kind, summary, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("{Strategy} backtest: {Trades} trades, {Win:0.0}% win, {Exp:0.000}R expectancy, PF {Pf:0.00}.",
            kind, summary.TotalTrades, summary.WinRatePct, summary.ExpectancyR, summary.ProfitFactor);
    }

    private async Task<StrategyParams> ParamsAsync(IStrategy strategy, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        return strategy.ParamsFor(strategy.Kind == StrategyKind.Swing ? settings.SwingRiskLevel : settings.MomentumRiskLevel);
    }

    /// <summary>Resolves every open paper trade of the strategy whose holding window has elapsed against fresh bars.</summary>
    private async Task ResolveOpenTradesAsync(StrategyKind kind, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var open = await db.PaperTrades.Where(t => t.Strategy == kind && t.Status == PaperTradeStatus.Open).ToListAsync(ct);
        if (open.Count == 0) return;

        var changed = 0;
        foreach (var t in open)
        {
            var hist = await SafeHistoryAsync(t.Ticker, HistoryRange.OneMonth, ct);
            if (hist is null) continue;

            // Sessions strictly after the generation day are the trade's life.
            var after = hist.Candles.Where(c => c.Time.Date > t.GeneratedAtUtc.Date).ToList();
            if (after.Count == 0) continue;

            var resolved = Resolve(t, after);
            if (resolved is null) continue; // still within the holding window

            var (status, exit) = resolved.Value;
            t.Status = status;
            t.ExitPrice = Math.Round(exit, 4);
            t.ResolvedAtUtc = clock.UtcNow;
            var risk = t.EntryReference - t.StopLoss;
            t.RealizedR = risk <= 0m ? 0m : Math.Round((exit - t.EntryReference) / risk, 3);
            changed++;
        }

        if (changed > 0) { await db.SaveChangesAsync(ct); logger?.LogInformation("Resolved {Count} {Strategy} paper trades.", changed, kind); }
    }

    /// <summary>
    /// Outcome of a trade given the sessions after entry: stop (checked first, conservative), target,
    /// or a time-based exit once the holding window passes. Null while still open.
    /// </summary>
    private static (PaperTradeStatus Status, decimal Exit)? Resolve(PaperTrade t, IReadOnlyList<Candle> after)
    {
        var window = Math.Min(t.HoldingDays, after.Count);
        for (var j = 0; j < window; j++)
        {
            if (after[j].Low <= t.StopLoss) return (PaperTradeStatus.HitStop, t.StopLoss);
            if (after[j].High >= t.Target) return (PaperTradeStatus.HitTarget, t.Target);
        }
        if (after.Count >= t.HoldingDays) return (PaperTradeStatus.TimeExit, after[t.HoldingDays - 1].Close);
        return null;
    }

    private async Task<IReadOnlyList<StrategyInput>> LoadUniverseAsync(StrategyKind kind, HistoryRange range, CancellationToken ct)
    {
        List<Stock> stocks;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            stocks = await db.Stocks.AsNoTracking()
                .Where(s => s.IsActive && (kind == StrategyKind.Swing ? s.IsSwingUniverse : s.IsMomentumUniverse))
                .ToListAsync(ct);

        var inputs = new List<StrategyInput>(stocks.Count);
        foreach (var s in stocks)
        {
            var hist = await SafeHistoryAsync(s.Ticker, range, ct, s.AssetClass);
            if (hist is { Candles.Count: > 0 })
                inputs.Add(new StrategyInput(s.Ticker, s.Name, s.Sector, s.AssetClass, hist.Candles));
        }
        return inputs;
    }

    private async Task<PriceHistory?> SafeHistoryAsync(string ticker, HistoryRange range, CancellationToken ct, AssetClass assetClass = AssetClass.Equity)
    {
        try { return await history.GetHistoryAsync(ticker, assetClass, range, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "History fetch failed for {Ticker}.", ticker); return null; }
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> TryGetSentimentAsync(StrategyKind kind, CancellationToken ct)
    {
        try
        {
            var map = await sentiment.GetTickerSentimentAsync(ct);
            return map.ToDictionary(kv => kv.Key, kv => kv.Value.MeanScore, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "{Strategy} sentiment lookup failed; scoring without it.", kind); return null; }
    }
}
