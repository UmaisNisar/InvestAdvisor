using System.Text.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

public sealed class PortfolioQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IFxRateProvider fx,
    IPriceHistoryProvider history,
    ITenantContext tenant) : IPortfolioQueries
{
    private static readonly JsonSerializerOptions _camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tid).OrderBy(h => h.Ticker).ToListAsync(ct);
        var tickers = holdings.Select(h => h.Ticker).Distinct().ToArray();

        var allSnaps = await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker))
            .OrderByDescending(s => s.FetchedAtUtc)
            .ToListAsync(ct);
        var latestSnaps = new Dictionary<string, PriceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in allSnaps)
            if (!latestSnaps.ContainsKey(s.Ticker)) latestSnaps[s.Ticker] = s;

        // The display currency is a per-tenant preference; default USD when no profile row exists.
        var displayCurrency = Cur(await db.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tid)
            .Select(p => p.DisplayCurrency)
            .FirstOrDefaultAsync(ct));

        var rates = await BuildRatesAsync(holdings, displayCurrency, ct);
        var views = BuildHoldingViews(holdings, latestSnaps, rates, out var totalMv);
        var totals = ComputeTotals(holdings, latestSnaps, rates);
        var allocation = BuildAllocation(holdings, views, totalMv);
        var movers = latestSnaps.Values
            .OrderByDescending(s => Math.Abs(s.PercentChange))
            .Take(5)
            .Select(s => new MoverView(s.Ticker, s.Price, s.PercentChange, s.PercentChange >= 0 ? "up" : "down"))
            .ToArray();

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
                DeserializeOrEmpty<PositionCall>(latestAdvice.ParsedPositionsJson));
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
            var cur = Cur(h.Currency);
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
            var flags = DeserializeOrEmpty<Flag>(r.ParsedFlagsJson);
            var drifts = DeserializeOrEmpty<DriftAlert>(r.ParsedDriftAlertsJson);
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
            Flags: DeserializeOrEmpty<Flag>(row.ParsedFlagsJson),
            DriftAlerts: DeserializeOrEmpty<DriftAlert>(row.ParsedDriftAlertsJson),
            Considerations: DeserializeOrEmpty<Consideration>(row.ParsedConsiderationsJson),
            Positions: DeserializeOrEmpty<PositionCall>(row.ParsedPositionsJson),
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

    private static IReadOnlyList<T> DeserializeOrEmpty<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<T>();
        try { return JsonSerializer.Deserialize<T[]>(json, _camel) ?? Array.Empty<T>(); }
        catch { return Array.Empty<T>(); }
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
        IReadOnlyList<Holding> holdings, string displayCurrency, CancellationToken ct)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        // Holding currencies convert values to USD; the display currency must be present too so
        // the UI can re-denominate USD totals even when no holding is priced in it.
        foreach (var c in holdings.Select(h => Cur(h.Currency)).Append(displayCurrency).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);
        return rates;
    }

    private static string Cur(string? c) => string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant();

    private static IReadOnlyList<HoldingView> BuildHoldingViews(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates,
        out decimal totalMarketValue)
    {
        totalMarketValue = 0m;
        // Per-holding Price/AvgCost stay in native currency (real quotes); market value / P&L are
        // converted to USD so totals and allocation are apples-to-apples.
        var pre = new List<(Holding h, decimal? mvUsd, decimal? price, decimal? pnlUsd, decimal? pnlPct, decimal? todayPct)>();
        foreach (var h in holdings)
        {
            var rate = rates.TryGetValue(Cur(h.Currency), out var r) ? r : 1m;
            snapshots.TryGetValue(h.Ticker, out var snap);
            decimal? price = snap?.Price;
            decimal? mvUsd = price is null ? null : h.Quantity * price.Value * rate;
            decimal costUsd = h.Quantity * h.AvgCost * rate;
            decimal? pnlUsd = mvUsd is null ? null : mvUsd - costUsd;
            decimal? pnlPct = (pnlUsd is null || costUsd == 0m) ? null : (pnlUsd / costUsd) * 100m;
            decimal? todayPct = snap?.PercentChange;
            if (mvUsd is not null) totalMarketValue += mvUsd.Value;
            pre.Add((h, mvUsd, price, pnlUsd, pnlPct, todayPct));
        }

        var list = new List<HoldingView>(pre.Count);
        foreach (var (h, mvUsd, price, pnlUsd, pnlPct, todayPct) in pre)
        {
            decimal? currentAlloc = (mvUsd is null || totalMarketValue == 0m) ? null : (mvUsd / totalMarketValue) * 100m;
            decimal? drift = (currentAlloc is null || h.TargetAllocationPct is null)
                ? null : currentAlloc - h.TargetAllocationPct;
            list.Add(new HoldingView(
                h.Ticker, h.Name, h.AssetClass.ToString(), h.AccountType.ToString(),
                h.Quantity, h.AvgCost,
                price, mvUsd, pnlUsd, pnlPct, todayPct,
                currentAlloc, h.TargetAllocationPct, drift, Cur(h.Currency)));
        }
        return list;
    }

    private static PortfolioTotals ComputeTotals(
        IReadOnlyList<Holding> holdings,
        IReadOnlyDictionary<string, PriceSnapshot> snapshots,
        IReadOnlyDictionary<string, decimal> rates)
    {
        decimal mv = 0, cost = 0, prev = 0;
        foreach (var h in holdings)
        {
            var rate = rates.TryGetValue(Cur(h.Currency), out var r) ? r : 1m;
            cost += h.Quantity * h.AvgCost * rate;
            if (snapshots.TryGetValue(h.Ticker, out var snap))
            {
                mv += h.Quantity * snap.Price * rate;
                prev += h.Quantity * snap.PreviousClose * rate;
            }
        }
        var pnl = mv - cost;
        return new PortfolioTotals(
            mv, cost, pnl,
            cost == 0 ? 0 : (pnl / cost) * 100m,
            mv - prev,
            prev == 0 ? 0 : ((mv - prev) / prev) * 100m);
    }

    private static AllocationView BuildAllocation(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<HoldingView> views,
        decimal totalMv)
    {
        var byClass = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var byAcct = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var i = 0; i < holdings.Count; i++)
        {
            var v = views[i].MarketValueUsd;
            if (v is null) continue;
            var ac = holdings[i].AssetClass.ToString();
            var at = holdings[i].AccountType.ToString();
            byClass[ac] = byClass.GetValueOrDefault(ac) + v.Value;
            byAcct[at] = byAcct.GetValueOrDefault(at) + v.Value;
        }
        if (totalMv > 0)
        {
            foreach (var k in byClass.Keys.ToArray()) byClass[k] = byClass[k] / totalMv * 100m;
            foreach (var k in byAcct.Keys.ToArray()) byAcct[k] = byAcct[k] / totalMv * 100m;
        }
        var drifts = views
            .Where(v => v.DriftPct is not null && v.TargetAllocationPct is not null)
            .Select(v => new DriftRow(v.Ticker, v.CurrentAllocationPct ?? 0m, v.TargetAllocationPct!.Value, v.DriftPct!.Value))
            .OrderByDescending(d => Math.Abs(d.DriftPct))
            .ToArray();
        return new AllocationView(byClass, byAcct, drifts);
    }
}
