using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Swing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Drives the swing module each scan cycle: resolve open paper trades from fresh bars, then rank
/// the swing universe and log the top qualifying setups as new paper trades. Also runs the backtest
/// gate over a year of history. Bars come from <see cref="IPriceHistoryProvider"/> (Yahoo — covers
/// both US and TSX). Idempotent per UTC day so re-ticks don't double-log.
/// </summary>
public sealed class SwingService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IPriceHistoryProvider history,
    ISwingScoringService scoring,
    ISentimentScoringService sentiment,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock,
    ILogger<SwingService>? logger = null) : ISwingService
{
    /// <summary>How many near-setups to keep on the watchlist when no/few setups qualify.</summary>
    private const int WatchlistCount = 8;

    public async Task<int> GenerateSetupsAsync(bool force = false, CancellationToken ct = default)
    {
        var today = clock.UtcNow.Date;
        var p = SwingParams.For((await settingsStore.GetAsync(ct)).SwingRiskLevel);

        await ResolveOpenTradesAsync(ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!force && await db.PaperTrades.AnyAsync(t => t.GeneratedAtUtc == today, ct))
        {
            logger?.LogInformation("Swing setups already generated for {Day:yyyy-MM-dd}; skipping.", today);
            return 0;
        }

        // A manual re-scan refreshes today's snapshot: drop today's still-open setups so they're
        // regenerated at current levels. Resolved trades (the track record) are never touched.
        if (force)
        {
            var stale = await db.PaperTrades
                .Where(t => t.GeneratedAtUtc == today && t.Status == PaperTradeStatus.Open)
                .ToListAsync(ct);
            if (stale.Count > 0) { db.PaperTrades.RemoveRange(stale); await db.SaveChangesAsync(ct); }
        }

        // Two years of daily bars: the regime filter needs a full 200-day SMA.
        var universe = await LoadUniverseAsync(HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("Swing universe is empty or no bars fetched."); return 0; }

        var sentimentByTicker = await TryGetSentimentAsync(ct);
        var ranked = scoring.Rank(universe, sentimentByTicker, p);

        // Always refresh the watchlist (near-setups), even when nothing qualifies — so the page is
        // never blank. Replace today's snapshot.
        await RefreshWatchlistAsync(db, ranked, today, ct);

        var picks = ranked.Where(s => s.Qualifies).Take(p.SetupCount).ToList();
        if (picks.Count == 0) { logger?.LogInformation("No qualifying swing setups today."); return 0; }

        var added = 0;
        foreach (var s in picks)
        {
            // Unique (Ticker, GeneratedAtUtc=today) guards against a same-day re-run racing in.
            if (await db.PaperTrades.AnyAsync(t => t.Ticker == s.Ticker && t.GeneratedAtUtc == today, ct)) continue;
            db.PaperTrades.Add(new PaperTrade
            {
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
                CompositeScore = s.CompositeScore,
                Rationale = s.Setup.Rationale,
                Status = PaperTradeStatus.Open,
                // Signal context at entry — so each resolved trade is a labelled example of what
                // conditions did/didn't pay off (the outcome-tracking dataset).
                SignalRsi = s.Features.Rsi,
                RegimeDistancePct = s.Features.RegimeDistancePct,
                PullbackPct = s.Features.PullbackPct,
                RelativeVolume = s.Features.RelativeVolume,
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        logger?.LogInformation("Logged {Count} swing setups for {Day:yyyy-MM-dd}: {Tickers}.",
            added, today, string.Join(", ", picks.Select(x => x.Ticker)));
        return added;
    }

    /// <summary>
    /// Replaces today's watchlist with the near-setups: names in a confirmed up-trend that haven't
    /// pulled back far enough to trigger yet, closest first (lowest RSI). Keeps the page useful on
    /// days with no actionable setup.
    /// </summary>
    private async Task RefreshWatchlistAsync(InvestAdvisorDbContext db, IReadOnlyList<SwingScore> ranked, DateTime today, CancellationToken ct)
    {
        var existing = await db.SwingWatchItems.Where(w => w.GeneratedAtUtc == today).ToListAsync(ct);
        if (existing.Count > 0) db.SwingWatchItems.RemoveRange(existing);

        var near = ranked
            .Where(s => !s.Qualifies && s.Features.AboveRegime && s.Features.Rsi is not null)
            .OrderBy(s => s.Features.Rsi)
            .Take(WatchlistCount);

        foreach (var s in near)
            db.SwingWatchItems.Add(new SwingWatchItem
            {
                GeneratedAtUtc = today,
                Ticker = s.Ticker,
                Name = s.Name,
                Close = s.Features.Close,
                CompositeScore = s.CompositeScore,
                Rsi = s.Features.Rsi,
                RegimeDistancePct = s.Features.RegimeDistancePct,
                TrendDistancePct = s.Features.TrendDistancePct,
                Note = $"In an up-trend; RSI(3) {s.Features.Rsi:0}. Triggers if it dips a bit more.",
            });

        await db.SaveChangesAsync(ct);
    }

    public async Task RunBacktestAsync(CancellationToken ct = default)
    {
        var p = SwingParams.For((await settingsStore.GetAsync(ct)).SwingRiskLevel);
        var universe = await LoadUniverseAsync(HistoryRange.TwoYears, ct);
        if (universe.Count == 0) { logger?.LogWarning("Swing backtest skipped — no universe bars."); return; }

        var summary = SwingBacktester.Run(universe, p);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.SwingBacktestResults.Add(new SwingBacktestResult
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
        logger?.LogInformation("Swing backtest: {Trades} trades, {Win:0.0}% win, {Exp:0.000}R expectancy, PF {Pf:0.00}.",
            summary.TotalTrades, summary.WinRatePct, summary.ExpectancyR, summary.ProfitFactor);
    }

    /// <summary>Resolves every open paper trade whose holding window has elapsed against fresh bars.</summary>
    private async Task ResolveOpenTradesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var open = await db.PaperTrades.Where(t => t.Status == PaperTradeStatus.Open).ToListAsync(ct);
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

        if (changed > 0) { await db.SaveChangesAsync(ct); logger?.LogInformation("Resolved {Count} paper trades.", changed); }
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

    private async Task<IReadOnlyList<SwingInput>> LoadUniverseAsync(HistoryRange range, CancellationToken ct)
    {
        List<Stock> stocks;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            stocks = await db.Stocks.AsNoTracking()
                .Where(s => s.IsActive && s.IsSwingUniverse)
                .ToListAsync(ct);

        var inputs = new List<SwingInput>(stocks.Count);
        foreach (var s in stocks)
        {
            var hist = await SafeHistoryAsync(s.Ticker, range, ct, s.AssetClass);
            if (hist is { Candles.Count: > 0 })
                inputs.Add(new SwingInput(s.Ticker, s.Name, s.Sector, s.AssetClass, hist.Candles));
        }
        return inputs;
    }

    private async Task<PriceHistory?> SafeHistoryAsync(string ticker, HistoryRange range, CancellationToken ct, AssetClass assetClass = AssetClass.Equity)
    {
        try { return await history.GetHistoryAsync(ticker, assetClass, range, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "Swing history fetch failed for {Ticker}.", ticker); return null; }
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> TryGetSentimentAsync(CancellationToken ct)
    {
        try
        {
            var map = await sentiment.GetTickerSentimentAsync(ct);
            return map.ToDictionary(kv => kv.Key, kv => kv.Value.MeanScore, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "Swing sentiment lookup failed; scoring without it."); return null; }
    }
}
