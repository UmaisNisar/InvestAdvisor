using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Agent;

/// <summary>
/// Builds the daily shortlist from the composite ranking and runs the per-stock LLM analysis on
/// just those names (top opportunities + top risks). Persists one <see cref="StockAnalysis"/> per
/// stock per day; re-running the same day is a no-op for already-analysed tickers.
/// </summary>
public sealed class StockAnalysisService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IScreenerScoringService scoring,
    ILlmClient llm,
    ISystemClock clock,
    ILogger<StockAnalysisService>? logger = null) : IStockAnalysisService
{
    private const int TopOpportunities = 15;
    private const int TopRisks = 10;

    // Compact: this JSON is the per-stock LLM request context; indentation is billed whitespace.
    private static readonly JsonSerializerOptions _ctxJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public async Task<int> AnalyzeShortlistAsync(CancellationToken ct = default)
    {
        var ranked = await scoring.RankAsync(ct: ct);
        if (ranked.Count == 0) return 0;

        var shortlist = ranked.Take(TopOpportunities)
            .Concat(ranked.TakeLast(TopRisks))
            .DistinctBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rankByTicker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ranked.Count; i++) rankByTicker[ranked[i].Ticker] = i + 1;

        var todayUtc = clock.UtcNow.Date;
        HashSet<string> alreadyToday;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            alreadyToday = (await db.StockAnalyses.AsNoTracking()
                    .Where(a => a.GeneratedAtUtc >= todayUtc)
                    .Select(a => a.Ticker)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var analysed = 0;
        foreach (var s in shortlist)
        {
            ct.ThrowIfCancellationRequested();
            if (alreadyToday.Contains(s.Ticker)) continue;

            try
            {
                var contextJson = JsonSerializer.Serialize(new
                {
                    s.Ticker,
                    s.Name,
                    s.Sector,
                    s.CompositeScore,
                    Rank = rankByTicker[s.Ticker],
                    UniverseSize = ranked.Count,
                    s.Factors,
                    s.Snapshot,
                }, _ctxJson);

                var result = await llm.AnalyzeStockAsync(SystemPrompts.StockAnalysisDefault, contextJson, ct: ct);

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                db.StockAnalyses.Add(new StockAnalysis
                {
                    Ticker = s.Ticker,
                    GeneratedAtUtc = clock.UtcNow,
                    CompositeScore = s.CompositeScore,
                    Summary = result.Summary,
                    Thesis = result.Thesis,
                    BullishFactorsJson = JsonSerializer.Serialize(result.BullishFactors),
                    BearishFactorsJson = JsonSerializer.Serialize(result.BearishFactors),
                    KeyRisksJson = JsonSerializer.Serialize(result.KeyRisks),
                    Conviction = result.Conviction,
                    ConvictionLabel = result.ConvictionLabel,
                    Model = result.Model,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    LatencyMs = result.LatencyMs,
                });
                await db.SaveChangesAsync(ct);
                analysed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Stock analysis failed for {Ticker}; continuing.", s.Ticker);
            }
        }

        if (analysed > 0)
            logger?.LogInformation("Analysed {Count} shortlist stocks (top {Top} + bottom {Bottom}).",
                analysed, TopOpportunities, TopRisks);
        return analysed;
    }
}
