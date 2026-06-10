using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Agent;

/// <summary>
/// Computes AI spend by aggregating the model + token columns already stored on every run row
/// (<c>AdviceLog</c>, <c>DailyRecommendation</c>, <c>StockAnalysis</c>) through <see cref="ModelPricing"/>.
/// No dedicated cost table — the run rows are the history.
/// </summary>
public sealed class CostService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock) : ICostService
{
    private const string SourcePortfolio = "Portfolio agent";
    private const string SourceDailyRec = "Daily recommendation";
    private const string SourceStock = "Stock analysis";
    private const string SourceSentiment = "Sentiment scoring";

    private readonly record struct Row(DateTime Ts, string Source, string Trigger, string Model, long Input, long Output);

    private static decimal Usd(Row r) => ModelPricing.EstimateUsd(r.Model, r.Input, r.Output);

    private async Task<List<Row>> LoadAsync(DateTime sinceUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = new List<Row>();

        var advice = await db.AdviceLogs.AsNoTracking()
            .Where(a => a.TimestampUtc >= sinceUtc)
            .Select(a => new { a.TimestampUtc, a.Trigger, a.Model, a.InputTokens, a.OutputTokens })
            .ToListAsync(ct);
        foreach (var a in advice)
            rows.Add(new Row(a.TimestampUtc, SourcePortfolio, a.Trigger.ToString(), a.Model, a.InputTokens, a.OutputTokens));

        var recs = await db.DailyRecommendations.AsNoTracking()
            .Where(r => r.GeneratedAtUtc >= sinceUtc)
            .Select(r => new { r.GeneratedAtUtc, r.Model, r.InputTokens, r.OutputTokens })
            .ToListAsync(ct);
        foreach (var r in recs)
            rows.Add(new Row(r.GeneratedAtUtc, SourceDailyRec, "—", r.Model, r.InputTokens, r.OutputTokens));

        var stocks = await db.StockAnalyses.AsNoTracking()
            .Where(s => s.GeneratedAtUtc >= sinceUtc)
            .Select(s => new { s.GeneratedAtUtc, s.Model, s.InputTokens, s.OutputTokens })
            .ToListAsync(ct);
        foreach (var s in stocks)
            rows.Add(new Row(s.GeneratedAtUtc, SourceStock, "—", s.Model, s.InputTokens, s.OutputTokens));

        var sentiment = await db.SentimentRuns.AsNoTracking()
            .Where(s => s.GeneratedAtUtc >= sinceUtc)
            .Select(s => new { s.GeneratedAtUtc, s.Model, s.InputTokens, s.OutputTokens })
            .ToListAsync(ct);
        foreach (var s in sentiment)
            rows.Add(new Row(s.GeneratedAtUtc, SourceSentiment, "—", s.Model, s.InputTokens, s.OutputTokens));

        return rows;
    }

    public async Task<decimal> TodaySpendUsdAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(clock.UtcNow.Date, ct);
        return rows.Sum(Usd);
    }

    public async Task<bool> IsOverDailyBudgetAsync(CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (settings.DailyBudgetUsd <= 0m) return false; // 0 = unlimited
        return await TodaySpendUsdAsync(ct) >= settings.DailyBudgetUsd;
    }

    public async Task<CostReport> GetReportAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var today = clock.UtcNow.Date;
        var since = today.AddDays(-(days - 1));
        var rows = await LoadAsync(since, ct);
        var settings = await settingsStore.GetAsync(ct);

        var todayRows = rows.Where(r => r.Ts >= today).ToList();
        var weekRows = rows.Where(r => r.Ts >= today.AddDays(-6)).ToList();

        static CostLine Line(string label, IEnumerable<Row> group)
        {
            var list = group.ToList();
            return new CostLine(label, list.Count, list.Sum(x => x.Input), list.Sum(x => x.Output), list.Sum(Usd));
        }

        var bySource = rows.GroupBy(r => r.Source).Select(g => Line(g.Key, g))
            .OrderByDescending(l => l.Usd).ToList();
        var byTrigger = rows.Where(r => r.Source == SourcePortfolio).GroupBy(r => r.Trigger).Select(g => Line(g.Key, g))
            .OrderByDescending(l => l.Usd).ToList();
        var byModel = rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Model) ? "(unknown)" : r.Model).Select(g => Line(g.Key, g))
            .OrderByDescending(l => l.Usd).ToList();
        var daily = rows.GroupBy(r => r.Ts.Date).Select(g => new CostDay(g.Key, g.Count(), g.Sum(Usd)))
            .OrderByDescending(d => d.Date).ToList();

        return new CostReport(
            TodayUsd: todayRows.Sum(Usd),
            Last7DaysUsd: weekRows.Sum(Usd),
            WindowUsd: rows.Sum(Usd),
            TodayRuns: todayRows.Count,
            Last7DaysRuns: weekRows.Count,
            WindowDays: days,
            DailyBudgetUsd: settings.DailyBudgetUsd,
            BySource: bySource,
            ByTrigger: byTrigger,
            ByModel: byModel,
            Daily: daily);
    }
}
