using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Agent;

/// <summary>
/// Builds the candidate set across all asset classes and runs ONE consolidated LLM call asking
/// where to invest today. This is the screener's only routine LLM spend (the per-stock analysis
/// pass is intentionally not run). One <see cref="DailyRecommendation"/> per day; idempotent.
/// </summary>
public sealed class DailyRecommendationService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IScreenerScoringService scoring,
    IAnthropicClient anthropic,
    ISystemClock clock,
    ILogger<DailyRecommendationService>? logger = null) : IDailyRecommendationService
{
    private const int StockCandidates = 12;
    private const int EtfCandidates = 8;
    private const int CryptoCandidates = 8;

    // Compact: this JSON is the LLM request context, so indentation would just be billed
    // whitespace. (SerializePicks below uses its own default serializer for stored columns.)
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public async Task<bool> GenerateAsync(int tenantId, bool force = false, CancellationToken ct = default)
    {
        var todayUtc = clock.UtcNow.Date;
        Profile? profile;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (!force && await db.DailyRecommendations.AsNoTracking().AnyAsync(r => r.TenantId == tenantId && r.GeneratedAtUtc >= todayUtc, ct))
                return false; // already done today for this tenant
            profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        }

        var stocks = (await scoring.RankAsync(AssetClass.Equity, ct)).Take(StockCandidates).ToList();
        var etfs = (await scoring.RankAsync(AssetClass.Etf, ct)).Take(EtfCandidates).ToList();
        var crypto = (await scoring.RankAsync(AssetClass.Crypto, ct)).Take(CryptoCandidates).ToList();
        if (stocks.Count == 0 && etfs.Count == 0 && crypto.Count == 0) return false;

        var medianPe = MedianPe(stocks);
        var contextJson = JsonSerializer.Serialize(new
        {
            profile = profile is null ? null : new
            {
                goals = profile.GoalsText,
                riskTolerance = profile.RiskTolerance.ToString(),
                timeHorizon = profile.TimeHorizon.ToString(),
            },
            valuationBackdrop = medianPe is { } pe ? $"equity universe median P/E {pe:0.0}" : null,
            stocks = stocks.Select(Project),
            etfs = etfs.Select(Project),
            crypto = crypto.Select(Project),
        }, _json);

        DailyRecommendationResult result;
        try
        {
            result = await anthropic.RecommendAllocationAsync(SystemPrompts.DailyAllocationDefault, contextJson, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Daily recommendation LLM call failed.");
            return false;
        }

        var stockNames = NameMap(stocks);
        var etfNames = NameMap(etfs);
        var cryptoNames = NameMap(crypto);

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (force)
            {
                var todays = await db.DailyRecommendations.Where(r => r.TenantId == tenantId && r.GeneratedAtUtc >= todayUtc).ToListAsync(ct);
                if (todays.Count > 0) db.DailyRecommendations.RemoveRange(todays); // replace today's
            }
            db.DailyRecommendations.Add(new DailyRecommendation
            {
                TenantId = tenantId,
                GeneratedAtUtc = clock.UtcNow,
                Summary = result.Summary,
                Caution = result.Caution,
                StocksJson = SerializePicks(result.Stocks, stockNames),
                EtfsJson = SerializePicks(result.Etfs, etfNames),
                CryptoJson = SerializePicks(result.Crypto, cryptoNames),
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                LatencyMs = result.LatencyMs,
            });
            await db.SaveChangesAsync(ct);
        }

        logger?.LogInformation(
            "Daily recommendation generated: {Stocks} stocks, {Etfs} ETFs, {Crypto} crypto ({In}+{Out} tokens).",
            result.Stocks.Count, result.Etfs.Count, result.Crypto.Count, result.InputTokens, result.OutputTokens);
        return true;
    }

    private static object Project(StockScore s) => new
    {
        s.Ticker,
        s.Name,
        s.Sector,
        compositeScore = s.CompositeScore,
        factors = s.Factors,
        snapshot = s.Snapshot,
    };

    private static Dictionary<string, string> NameMap(IEnumerable<StockScore> scores) =>
        scores.GroupBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
              .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

    // Keep only picks whose ticker really was a candidate (guards against hallucinated tickers).
    private static string SerializePicks(IReadOnlyList<RecommendedPick> picks, Dictionary<string, string> names) =>
        JsonSerializer.Serialize(picks
            .Where(p => names.ContainsKey(p.Ticker))
            .Select(p => new { ticker = p.Ticker, name = names[p.Ticker], reason = p.Reason })
            .ToList());

    private static decimal? MedianPe(IReadOnlyList<StockScore> stocks)
    {
        var pes = stocks.Select(s => s.Snapshot.PeRatio).Where(p => p is > 0m).Select(p => p!.Value)
            .OrderBy(x => x).ToList();
        if (pes.Count == 0) return null;
        var mid = pes.Count / 2;
        return pes.Count % 2 == 1 ? pes[mid] : (pes[mid - 1] + pes[mid]) / 2m;
    }
}
