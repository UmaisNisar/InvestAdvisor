using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Computes a composite factor score (0–100) for every active universe member of one asset class,
/// ranked best-first. Equities use the full six-factor model; ETFs and crypto use a thinner,
/// momentum-based set (they have no fundamentals/analyst/insider data).
/// </summary>
public interface IScreenerScoringService
{
    Task<IReadOnlyList<StockScore>> RankAsync(AssetClass assetClass = AssetClass.Equity, CancellationToken ct = default);
}

/// <summary>One ranked stock: the composite, its six sub-scores, and the raw values behind them.</summary>
public sealed record StockScore(
    string Ticker,
    string Name,
    string Sector,
    decimal CompositeScore,
    FactorScores Factors,
    StockSnapshot Snapshot);

/// <summary>Each sub-score is 0–100 (universe percentile) or null when the stock lacks that data.</summary>
public sealed record FactorScores(
    decimal? Valuation,
    decimal? Growth,
    decimal? Quality,
    decimal? Analyst,
    decimal? Insider,
    decimal? Momentum,
    decimal? Sentiment);

/// <summary>The underlying numbers, surfaced for display, digests, and the LLM context.</summary>
public sealed record StockSnapshot(
    decimal? PeRatio,
    decimal? PriceToFreeCashFlow,
    decimal? RevenueGrowthPct,
    decimal? EpsGrowthPct,
    decimal? DebtToEquity,
    decimal? PriceReturn13Week,
    int AnalystTotal,
    int AnalystBuyCount,
    decimal AnalystBuyPct,
    int AnalystTrendDelta,
    decimal NetInsiderShares,
    DateTime? DataAsOfUtc);
