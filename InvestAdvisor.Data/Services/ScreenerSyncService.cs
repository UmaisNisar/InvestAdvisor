using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Pulls screener data for the active universe and persists new snapshots, by asset class:
/// equities get fundamentals + analyst ratings + insider trades (Finnhub); ETFs get fundamentals
/// only (momentum + beta); crypto gets price + market cap + 7-/30-day momentum (CoinGecko, one
/// batched call). Saves per ticker so a single failure never loses prior progress.
/// </summary>
public sealed class ScreenerSyncService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IScreenerDataProvider provider,
    ICryptoMarketProvider crypto,
    ILogger<ScreenerSyncService>? logger = null) : IScreenerSyncService
{
    private static readonly TimeSpan InsiderWindow = TimeSpan.FromDays(120);

    public async Task<ScreenerSyncResult> SyncAsync(CancellationToken ct = default)
    {
        List<StockRef> stocks;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            stocks = await db.Stocks.AsNoTracking()
                .Where(s => s.IsActive).OrderBy(s => s.Ticker)
                .Select(s => new StockRef(s.Ticker, s.AssetClass, s.ExternalId))
                .ToListAsync(ct);

        int processed = 0, metrics = 0, ratings = 0, insider = 0;
        var now = DateTime.UtcNow;
        var insiderCutoff = now - InsiderWindow;

        // --- Equities + ETFs (Finnhub) ---
        foreach (var s in stocks.Where(s => s.AssetClass != AssetClass.Crypto))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var f = await provider.GetFundamentalsAsync(s.Ticker, ct);
                AnalystRatingResult? r = null;
                IReadOnlyList<InsiderTradeResult> trades = Array.Empty<InsiderTradeResult>();
                if (s.AssetClass == AssetClass.Equity) // analyst/insider aren't meaningful for ETFs
                {
                    r = await provider.GetLatestAnalystRatingAsync(s.Ticker, ct);
                    trades = await provider.GetInsiderTradesAsync(s.Ticker, ct);
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                if (f is not null)
                {
                    db.StockMetrics.Add(new StockMetric
                    {
                        Ticker = s.Ticker,
                        FetchedAtUtc = now,
                        MarketCap = f.MarketCap,
                        PeRatio = f.PeRatio,
                        RevenueGrowthPct = f.RevenueGrowthPct,
                        EpsGrowthPct = f.EpsGrowthPct,
                        DebtToEquity = f.DebtToEquity,
                        PriceToFreeCashFlow = f.PriceToFreeCashFlow,
                        MomentumShort = f.MomentumShort,
                        MomentumLong = f.MomentumLong,
                        Beta = f.Beta,
                        RawJson = f.RawJson,
                    });
                    metrics++;
                }

                if (r is not null &&
                    !await db.AnalystRatings.AnyAsync(a => a.Ticker == s.Ticker && a.Period == r.Period, ct))
                {
                    db.AnalystRatings.Add(new AnalystRating
                    {
                        Ticker = s.Ticker, Period = r.Period, FetchedAtUtc = now,
                        StrongBuy = r.StrongBuy, Buy = r.Buy, Hold = r.Hold, Sell = r.Sell, StrongSell = r.StrongSell,
                    });
                    ratings++;
                }

                if (trades.Count > 0)
                {
                    var existing = await db.InsiderTrades.AsNoTracking()
                        .Where(t => t.Ticker == s.Ticker && t.FilingDate >= insiderCutoff)
                        .Select(t => new { t.Name, t.FilingDate, t.Change, t.TransactionCode })
                        .ToListAsync(ct);
                    var seen = new HashSet<string>(existing.Select(e => Key(e.Name, e.FilingDate, e.Change, e.TransactionCode)));
                    foreach (var t in trades)
                    {
                        if (t.FilingDate < insiderCutoff) continue;
                        if (!seen.Add(Key(t.Name, t.FilingDate, t.Change, t.TransactionCode))) continue;
                        db.InsiderTrades.Add(new InsiderTrade
                        {
                            Ticker = s.Ticker, Name = t.Name, Change = t.Change, Shares = t.Shares,
                            FilingDate = t.FilingDate, TransactionCode = t.TransactionCode, IsDerivative = t.IsDerivative,
                            FetchedAtUtc = now,
                        });
                        insider++;
                    }
                }

                await db.SaveChangesAsync(ct);
                processed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Screener sync failed for {Ticker}; continuing.", s.Ticker);
            }
        }

        // --- Crypto (CoinGecko, one batched call) ---
        var cryptos = stocks.Where(s => s.AssetClass == AssetClass.Crypto && !string.IsNullOrEmpty(s.ExternalId)).ToList();
        if (cryptos.Count > 0)
        {
            try
            {
                var markets = await crypto.GetMarketsAsync(cryptos.Select(c => c.ExternalId!).ToArray(), ct);
                var byId = markets.ToDictionary(m => m.CoinId, StringComparer.OrdinalIgnoreCase);

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                foreach (var c in cryptos)
                {
                    if (!byId.TryGetValue(c.ExternalId!, out var m)) continue;
                    db.StockMetrics.Add(new StockMetric
                    {
                        Ticker = c.Ticker,
                        FetchedAtUtc = now,
                        MarketCap = m.MarketCap,
                        MomentumShort = m.Return7d,
                        MomentumLong = m.Return30d,
                        RawJson = "{}",
                    });
                    metrics++;
                    processed++;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Crypto sync (CoinGecko) failed.");
            }
        }

        logger?.LogInformation(
            "Screener sync complete: {Processed}/{Total} names, {Metrics} metrics, {Ratings} ratings, {Insider} insider trades.",
            processed, stocks.Count, metrics, ratings, insider);
        return new ScreenerSyncResult(processed, metrics, ratings, insider);
    }

    private static string Key(string name, DateTime filingDate, decimal change, string code) =>
        $"{name}|{filingDate:O}|{change}|{code}";

    private sealed record StockRef(string Ticker, AssetClass AssetClass, string? ExternalId);
}
