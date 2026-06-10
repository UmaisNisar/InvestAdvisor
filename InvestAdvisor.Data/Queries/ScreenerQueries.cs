using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Queries;

/// <summary>
/// Builds the Screener read models. Recomputes the composite ranking live (so it reflects the
/// current factor weights), attaches each stock's latest LLM analysis, exposes a universe
/// valuation gauge, and surfaces top picks chosen by composite strength (not daily movement).
/// </summary>
public sealed class ScreenerQueries(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IScreenerScoringService scoring,
    ITenantContext tenant) : IScreenerQueries
{
    public async Task<ScreenerView> GetAsync(AssetClass assetClass = AssetClass.Equity, int topCount = 15, int bottomCount = 10, CancellationToken ct = default)
    {
        var ranked = await scoring.RankAsync(assetClass, ct);
        if (ranked.Count == 0)
            return new ScreenerView(0, null, null, Array.Empty<ScreenerEntry>(), Array.Empty<ScreenerEntry>());

        Dictionary<string, StockAnalysisView> analyses;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            analyses = await LoadLatestAnalysesAsync(db, ct);

        var maxAsOf = ranked.Select(s => s.Snapshot.DataAsOfUtc)
            .Where(d => d.HasValue).Select(d => d!.Value)
            .DefaultIfEmpty().Max();
        DateTime? asOf = maxAsOf == default ? null : maxAsOf;

        ScreenerEntry Entry(StockScore s, int idx) => new(idx + 1, s, analyses.GetValueOrDefault(s.Ticker));

        var opportunities = ranked.Take(topCount).Select((s, i) => Entry(s, i)).ToList();
        var start = Math.Max(0, ranked.Count - bottomCount);
        var risks = ranked.Skip(start).Select((s, i) => Entry(s, start + i)).ToList();

        return new ScreenerView(ranked.Count, asOf, UniverseMedianPe(ranked), opportunities, risks);
    }

    public async Task<DailyRecommendationView?> GetDailyRecommendationAsync(CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var latest = await db.DailyRecommendations.AsNoTracking()
            .Where(r => r.TenantId == tid)
            .OrderByDescending(r => r.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (latest is null) return null;

        return new DailyRecommendationView(
            latest.GeneratedAtUtc,
            latest.Summary,
            latest.Caution,
            DeserPicks(latest.StocksJson),
            DeserPicks(latest.EtfsJson),
            DeserPicks(latest.CryptoJson));
    }

    private static IReadOnlyList<RecommendationPick> DeserPicks(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<PickDto>>(json);
            return raw is null
                ? Array.Empty<RecommendationPick>()
                : raw.Select(p => new RecommendationPick(p.ticker ?? "", p.name ?? "", p.reason ?? "", p.priceAtRecommendation)).ToList();
        }
        catch { return Array.Empty<RecommendationPick>(); }
    }

    private sealed record PickDto(string? ticker, string? name, string? reason, decimal? priceAtRecommendation = null);

    public async Task<ScreenerValidation?> GetValidationAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var priced = await db.ScreenerScores.AsNoTracking()
            .Where(s => s.Price != null)
            .Select(s => new { s.Ticker, s.AsOfDate, s.Rank, Price = s.Price!.Value })
            .ToListAsync(ct);

        var dates = priced.Select(p => p.AsOfDate).Distinct().OrderBy(d => d).ToList();
        if (dates.Count < 2) return null;

        var fromDate = dates.First();
        var toDate = dates.Last();
        var fromByTicker = priced.Where(p => p.AsOfDate == fromDate)
            .ToDictionary(p => p.Ticker, p => p, StringComparer.OrdinalIgnoreCase);
        var toByTicker = priced.Where(p => p.AsOfDate == toDate)
            .ToDictionary(p => p.Ticker, p => p.Price, StringComparer.OrdinalIgnoreCase);

        var rows = new List<(int RankThen, decimal ReturnPct)>();
        foreach (var (ticker, f) in fromByTicker)
            if (f.Price > 0m && toByTicker.TryGetValue(ticker, out var pNow))
                rows.Add((f.Rank, (pNow - f.Price) / f.Price * 100m));

        if (rows.Count < 8) return null; // too few names to mean anything

        var universeReturn = rows.Average(r => r.ReturnPct);
        var topGroupSize = Math.Max(5, rows.Count / 10); // ~top decile, min 5
        var topReturn = rows.OrderBy(r => r.RankThen).Take(topGroupSize).Average(r => r.ReturnPct);

        return new ScreenerValidation(fromDate, toDate, rows.Count, topGroupSize,
            Math.Round(topReturn, 2), Math.Round(universeReturn, 2));
    }

    private static decimal? UniverseMedianPe(IReadOnlyList<StockScore> ranked)
    {
        var pes = ranked.Select(s => s.Snapshot.PeRatio).Where(p => p is > 0m).Select(p => p!.Value)
            .OrderBy(x => x).ToList();
        if (pes.Count == 0) return null;
        var mid = pes.Count / 2;
        return pes.Count % 2 == 1 ? pes[mid] : (pes[mid - 1] + pes[mid]) / 2m;
    }

    private static async Task<Dictionary<string, StockAnalysisView>> LoadLatestAnalysesAsync(
        InvestAdvisorDbContext db, CancellationToken ct)
    {
        var rows = await db.StockAnalyses.AsNoTracking()
            .OrderByDescending(a => a.GeneratedAtUtc)
            .ToListAsync(ct);
        var map = new Dictionary<string, StockAnalysisView>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (map.ContainsKey(r.Ticker)) continue;
            map[r.Ticker] = new StockAnalysisView(
                r.GeneratedAtUtc, r.Summary, r.Thesis,
                Deser(r.BullishFactorsJson), Deser(r.BearishFactorsJson), Deser(r.KeyRisksJson),
                r.Conviction, r.ConvictionLabel);
        }
        return map;
    }

    private static IReadOnlyList<string> Deser(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
