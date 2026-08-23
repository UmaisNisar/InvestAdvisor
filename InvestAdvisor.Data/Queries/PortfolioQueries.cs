using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

public sealed class PortfolioQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IFxRateProvider fx,
    IPriceHistoryProvider history,
    ITenantContext tenant) : IPortfolioQueries
{
    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tid).OrderBy(h => h.Ticker).ToListAsync(ct);
        var tickers = holdings.Select(h => h.Ticker).Distinct().ToArray();

        var latestSnaps = PortfolioCalculator.LatestByTicker(await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker))
            .OrderByDescending(s => s.FetchedAtUtc)
            .ToListAsync(ct));

        // The display currency is a per-tenant preference; default USD when no profile row exists.
        var displayCurrency = Currency.Normalize(await db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tid)
            .Select(p => p.DisplayCurrency)
            .FirstOrDefaultAsync(ct));

        var realized = await db.RealizedLots.AsNoTracking().Where(r => r.TenantId == tid).ToListAsync(ct);
        var rates = await BuildRatesAsync(holdings.Select(h => h.Currency).Concat(realized.Select(r => r.Currency)), displayCurrency, ct);
        var (views, totalMv) = PortfolioCalculator.HoldingViews(holdings, latestSnaps, rates);
        var totals = PortfolioCalculator.Totals(holdings, latestSnaps, rates, PortfolioCalculator.RealizedPnlUsd(realized, rates));
        var allocation = PortfolioCalculator.Allocation(holdings, views, totalMv);
        var movers = PortfolioCalculator.TopMovers(latestSnaps.Values, 5);

        var latestAdvice = await db.AdviceLogs.AsNoTracking()
            .Where(a => a.TenantId == tid)
            .OrderByDescending(a => a.TimestampUtc)
            .FirstOrDefaultAsync(ct);

        LatestAdviceSummary? latest = null;
        if (latestAdvice is not null)
        {
            var flagCount = CountArray(latestAdvice.ParsedFlagsJson);
            var driftCount = CountArray(latestAdvice.ParsedDriftAlertsJson);
            latest = new LatestAdviceSummary(
                latestAdvice.Id, latestAdvice.TimestampUtc,
                latestAdvice.Trigger.ToString(), latestAdvice.TriggerDetail,
                latestAdvice.ParsedSummary, flagCount, driftCount,
                JsonOptions.ArrayOrEmpty<PositionCall>(latestAdvice.ParsedPositionsJson));
        }

        return new DashboardSnapshot(totals, views, allocation, movers, latest, rates, displayCurrency);
    }

    public async Task<PortfolioValueHistory> GetValueHistoryAsync(HistoryRange range, CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tid).ToListAsync(ct);

        // The same ticker can sit in several accounts; the chart only needs total shares per ticker.
        var positions = holdings
            .GroupBy(h => h.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Ticker: g.Key, g.First().AssetClass, Quantity: g.Sum(h => h.Quantity)))
            .ToArray();

        var fetches = positions
            .Select(async p => (p.Ticker, p.Quantity, History: await history.GetHistoryAsync(p.Ticker, p.AssetClass, range, ct)))
            .ToArray();
        await Task.WhenAll(fetches);

        var intraday = range is HistoryRange.OneDay or HistoryRange.OneWeek;
        var missing = new List<string>();
        var series = new List<(decimal Qty, decimal RateToUsd, IReadOnlyList<(DateTime Time, decimal Close)> Bars)>();
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        foreach (var f in fetches)
        {
            var (ticker, qty, h) = f.Result;
            if (h is null || h.Candles.Count == 0) { missing.Add(ticker); continue; }
            var cur = Currency.Normalize(h.Currency);
            if (!rates.ContainsKey(cur)) rates[cur] = await fx.GetRateToUsdAsync(cur, ct);
            // Daily bars: collapse to the session date so exchanges with different open times
            // (e.g. Toronto vs. New York) land on the same point instead of stair-stepping.
            series.Add((qty, rates[cur],
                h.Candles.Select(c => (intraday ? c.Time : c.Time.Date, c.Close)).ToArray()));
        }
        if (series.Count == 0) return new PortfolioValueHistory(Array.Empty<PortfolioValuePoint>(), missing);

        // Merge onto a shared timeline, forward-filling each ticker's last close across the other
        // tickers' timestamps. Before a series starts (new listing mid-window) its first close is
        // backfilled flat so the portfolio line doesn't jump when the series begins.
        var timeline = series.SelectMany(s => s.Bars.Select(b => b.Time)).Distinct().OrderBy(t => t).ToArray();
        var totals = new decimal[timeline.Length];
        foreach (var (qty, rate, bars) in series)
        {
            var i = 0;
            var close = bars[0].Close;
            for (var t = 0; t < timeline.Length; t++)
            {
                while (i < bars.Count && bars[i].Time <= timeline[t]) close = bars[i++].Close;
                totals[t] += qty * close * rate;
            }
        }
        var points = timeline.Select((t, idx) => new PortfolioValuePoint(t, totals[idx])).ToArray();
        return new PortfolioValueHistory(points, missing);
    }

    public async Task<IReadOnlyList<RealizedLotView>> GetRealizedLotsAsync(CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lots = await db.RealizedLots.AsNoTracking()
            .Where(r => r.TenantId == tid)
            .OrderByDescending(r => r.RealizedAtUtc)
            .ToListAsync(ct);

        var rates = await BuildRatesAsync(lots.Select(l => l.Currency), "USD", ct);
        return lots.Select(l =>
        {
            var pnl = l.Proceeds - l.CostBasis;
            var rate = rates.TryGetValue(Currency.Normalize(l.Currency), out var r) ? r : 1m;
            return new RealizedLotView(
                l.Id, l.Ticker, l.Name, l.AssetClass.ToString(), l.AccountType.ToString(),
                l.Quantity, l.Proceeds, l.CostBasis, pnl, pnl * rate,
                l.CostBasis == 0m ? 0m : (pnl / l.CostBasis) * 100m,
                Currency.Normalize(l.Currency), l.RealizedAtUtc, l.ManualEntry);
        }).ToList();
    }

    public async Task<AdvicePage> GetAdvicePageAsync(int skip, int take, CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var total = await db.AdviceLogs.AsNoTracking().CountAsync(a => a.TenantId == tid, ct);
        var rows = await db.AdviceLogs.AsNoTracking()
            .Where(a => a.TenantId == tid)
            .OrderByDescending(a => a.TimestampUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var flags = JsonOptions.ArrayOrEmpty<Flag>(r.ParsedFlagsJson);
            var drifts = JsonOptions.ArrayOrEmpty<DriftAlert>(r.ParsedDriftAlertsJson);
            return new AdviceLogSummaryView(
                Id: r.Id,
                TimestampUtc: r.TimestampUtc,
                Trigger: r.Trigger,
                TriggerDetail: r.TriggerDetail,
                Summary: r.ParsedSummary,
                FlagCount: flags.Count,
                CriticalFlagCount: flags.Count(f => f.Severity == FlagSeverity.Critical),
                WarnFlagCount: flags.Count(f => f.Severity == FlagSeverity.Warn),
                DriftAlertCount: drifts.Count,
                ActionSuggestedDriftCount: drifts.Count(d => d.Severity == DriftSeverity.ActionSuggested),
                ReplayOfAdviceLogId: r.ReplayOfAdviceLogId);
        }).ToArray();

        return new AdvicePage(items, total);
    }

    public async Task<AdviceLogDetailView?> GetAdviceDetailAsync(long id, CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AdviceLogs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.TenantId == tid, ct);
        if (row is null) return null;

        return new AdviceLogDetailView(
            Id: row.Id,
            TimestampUtc: row.TimestampUtc,
            Trigger: row.Trigger,
            TriggerDetail: row.TriggerDetail,
            Summary: row.ParsedSummary,
            Flags: JsonOptions.ArrayOrEmpty<Flag>(row.ParsedFlagsJson),
            DriftAlerts: JsonOptions.ArrayOrEmpty<DriftAlert>(row.ParsedDriftAlertsJson),
            Considerations: JsonOptions.ArrayOrEmpty<Consideration>(row.ParsedConsiderationsJson),
            Positions: JsonOptions.ArrayOrEmpty<PositionCall>(row.ParsedPositionsJson),
            SystemPromptUsed: row.SystemPromptUsed,
            StructuredInputJson: row.StructuredInputJson,
            RawResponseText: row.RawResponseText,
            Model: row.Model,
            InputTokens: row.InputTokens,
            OutputTokens: row.OutputTokens,
            LatencyMs: row.LatencyMs,
            ParseFallbackUsed: row.ParseFallbackUsed,
            ReplayOfAdviceLogId: row.ReplayOfAdviceLogId);
    }

    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbOk = await db.Database.CanConnectAsync(ct);
        var lastSnap = await db.PriceSnapshots.AsNoTracking()
            .OrderByDescending(s => s.FetchedAtUtc)
            .Select(s => (DateTime?)s.FetchedAtUtc).FirstOrDefaultAsync(ct);
        var lastAdvice = await db.AdviceLogs.AsNoTracking()
            .Where(a => a.ParsedSummary != "[error] Agent run failed; see RawResponseText for details.")
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => (DateTime?)a.TimestampUtc).FirstOrDefaultAsync(ct);
        var totalAdvice = await db.AdviceLogs.AsNoTracking().CountAsync(ct);
        var totalHoldings = await db.Holdings.AsNoTracking().CountAsync(ct);
        return new HealthStatus(dbOk, lastSnap, lastAdvice, totalAdvice, totalHoldings);
    }


    private static int CountArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch { return 0; }
    }

    private async Task<Dictionary<string, decimal>> BuildRatesAsync(
        IEnumerable<string> currencies, string displayCurrency, CancellationToken ct)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        // Holding (and realized-lot) currencies convert values to USD; the display currency must be
        // present too so the UI can re-denominate USD totals even when nothing is priced in it.
        foreach (var c in currencies.Select(Currency.Normalize).Append(displayCurrency).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);
        return rates;
    }
}
