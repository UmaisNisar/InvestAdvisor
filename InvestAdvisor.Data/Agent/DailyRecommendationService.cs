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
/// where to invest today. The call also sees the tenant's current portfolio (tickers +
/// allocation shares) so picks account for what is already owned. This is the screener's only
/// routine LLM spend (the per-stock analysis pass is intentionally not run). One
/// <see cref="DailyRecommendation"/> per day; idempotent.
/// </summary>
public sealed class DailyRecommendationService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IScreenerScoringService scoring,
    IAnthropicClient anthropic,
    IFxRateProvider fx,
    IMarketDataProvider market,
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

        var portfolio = await BuildPortfolioContextAsync(tenantId, ct);

        // Recommendations are personalized by profile AND portfolio, so an LLM call can only be
        // reused across tenants when both match. Empty portfolios trivially match — the common
        // case of several freshly-onboarded tenants sharing the default profile.
        if (!force && portfolio is null && profile is not null
            && await TryCloneMatchingTenantRecAsync(tenantId, profile, todayUtc, ct))
            return true;

        var rankedStocks = await scoring.RankAsync(AssetClass.Equity, ct);
        var stocks = rankedStocks.Take(StockCandidates).ToList();
        var etfs = (await scoring.RankAsync(AssetClass.Etf, ct)).Take(EtfCandidates).ToList();
        var crypto = (await scoring.RankAsync(AssetClass.Crypto, ct)).Take(CryptoCandidates).ToList();
        if (stocks.Count == 0 && etfs.Count == 0 && crypto.Count == 0) return false;

        // Median over the WHOLE ranked universe — the top candidates skew cheap by construction,
        // so a median over just them would misstate the valuation backdrop.
        var medianPe = MedianPe(rankedStocks);
        var contextJson = JsonSerializer.Serialize(new
        {
            profile = profile is null ? null : new
            {
                goals = profile.GoalsText,
                riskTolerance = profile.RiskTolerance.ToString(),
                timeHorizon = profile.TimeHorizon.ToString(),
            },
            portfolio,
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

        // Price each pick now so score-vs-forward-return validation is possible later.
        var stockPrices = await PriceMapAsync(result.Stocks, stockNames, AssetClass.Equity, ct);
        var etfPrices = await PriceMapAsync(result.Etfs, etfNames, AssetClass.Etf, ct);
        var cryptoPrices = await PriceMapAsync(result.Crypto, cryptoNames, AssetClass.Crypto, ct);

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
                StocksJson = SerializePicks(result.Stocks, stockNames, stockPrices),
                EtfsJson = SerializePicks(result.Etfs, etfNames, etfPrices),
                CryptoJson = SerializePicks(result.Crypto, cryptoNames, cryptoPrices),
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

    /// <summary>
    /// Compact current-portfolio summary for the LLM: total USD value plus each ticker's
    /// allocation share. Null when the tenant holds nothing (the property is then omitted
    /// from the context JSON entirely).
    /// </summary>
    private async Task<object?> BuildPortfolioContextAsync(int tenantId, CancellationToken ct)
    {
        List<Holding> holdings;
        Dictionary<string, decimal> priceByTicker;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tenantId).ToListAsync(ct);
            if (holdings.Count == 0) return null;

            var tickers = holdings.Select(h => h.Ticker).Distinct().ToList();
            priceByTicker = (await db.PriceSnapshots.AsNoTracking()
                    .Where(s => tickers.Contains(s.Ticker))
                    .OrderByDescending(s => s.FetchedAtUtc)
                    .ToListAsync(ct))
                .GroupBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Price, StringComparer.OrdinalIgnoreCase);
        }

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
        foreach (var c in holdings.Select(h => Cur(h.Currency)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!rates.ContainsKey(c)) rates[c] = await fx.GetRateToUsdAsync(c, ct);

        var mvByTicker = new Dictionary<string, (string AssetClass, decimal Mv)>(StringComparer.OrdinalIgnoreCase);
        decimal total = 0m;
        foreach (var h in holdings)
        {
            if (!priceByTicker.TryGetValue(h.Ticker, out var price)) continue;
            var mv = h.Quantity * price * rates.GetValueOrDefault(Cur(h.Currency), 1m);
            total += mv;
            mvByTicker[h.Ticker] = mvByTicker.TryGetValue(h.Ticker, out var prev)
                ? (prev.AssetClass, prev.Mv + mv)
                : (h.AssetClass.ToString(), mv);
        }
        if (total <= 0m) return null;

        return new
        {
            totalMarketValueUsd = Math.Round(total, 0),
            positions = mvByTicker
                .OrderByDescending(kv => kv.Value.Mv)
                .Select(kv => new
                {
                    ticker = kv.Key,
                    assetClass = kv.Value.AssetClass,
                    allocationPct = Math.Round(kv.Value.Mv / total * 100m, 1),
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Copies today's recommendation from another tenant whose profile matches and whose
    /// portfolio is also empty. Saves one LLM call per duplicate-profile tenant per day.
    /// </summary>
    private async Task<bool> TryCloneMatchingTenantRecAsync(
        int tenantId, Profile profile, DateTime todayUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var matchingTenantIds = await db.Profiles.AsNoTracking()
            .Where(p => p.TenantId != tenantId
                        && p.GoalsText == profile.GoalsText
                        && p.RiskTolerance == profile.RiskTolerance
                        && p.TimeHorizon == profile.TimeHorizon)
            .Select(p => p.TenantId)
            .ToListAsync(ct);
        if (matchingTenantIds.Count == 0) return false;

        // Only tenants that also hold nothing — a portfolio makes the rec personalized.
        var tenantsWithHoldings = await db.Holdings.AsNoTracking()
            .Where(h => matchingTenantIds.Contains(h.TenantId))
            .Select(h => h.TenantId).Distinct().ToListAsync(ct);
        var emptyMatches = matchingTenantIds.Except(tenantsWithHoldings).ToList();
        if (emptyMatches.Count == 0) return false;

        var source = await db.DailyRecommendations.AsNoTracking()
            .Where(r => emptyMatches.Contains(r.TenantId) && r.GeneratedAtUtc >= todayUtc)
            .OrderByDescending(r => r.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (source is null) return false;

        db.DailyRecommendations.Add(new DailyRecommendation
        {
            TenantId = tenantId,
            GeneratedAtUtc = clock.UtcNow,
            Summary = source.Summary,
            Caution = source.Caution,
            StocksJson = source.StocksJson,
            EtfsJson = source.EtfsJson,
            CryptoJson = source.CryptoJson,
            Model = source.Model,
            InputTokens = 0, // reused — no new spend
            OutputTokens = 0,
            LatencyMs = 0,
        });
        await db.SaveChangesAsync(ct);
        logger?.LogInformation(
            "Daily recommendation for tenant {Tenant} reused from tenant {Source} (matching profile, empty portfolios).",
            tenantId, source.TenantId);
        return true;
    }

    private async Task<Dictionary<string, decimal>> PriceMapAsync(
        IReadOnlyList<RecommendedPick> picks,
        Dictionary<string, string> names,
        AssetClass assetClass,
        CancellationToken ct)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in picks.Where(p => names.ContainsKey(p.Ticker)))
        {
            try
            {
                var quote = await market.GetQuoteAsync(p.Ticker, assetClass, ct);
                if (quote is { Price: > 0m }) prices[p.Ticker] = quote.Price;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Quote fetch for pick {Ticker} failed; storing without price.", p.Ticker);
            }
        }
        return prices;
    }

    private static string Cur(string? c) => string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant();

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
    private static string SerializePicks(
        IReadOnlyList<RecommendedPick> picks,
        Dictionary<string, string> names,
        Dictionary<string, decimal> prices) =>
        JsonSerializer.Serialize(picks
            .Where(p => names.ContainsKey(p.Ticker))
            .Select(p => new
            {
                ticker = p.Ticker,
                name = names[p.Ticker],
                reason = p.Reason,
                priceAtRecommendation = prices.TryGetValue(p.Ticker, out var price) ? price : (decimal?)null,
            })
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
