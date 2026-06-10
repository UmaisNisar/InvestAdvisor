using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Read models for the Screener page and the dashboard highlight. Scores are <b>relative within
/// the curated universe</b> (percentile ranks), not absolute buy signals — the views carry a
/// universe valuation gauge and a score-vs-return validation so that's visible.
/// </summary>
public interface IScreenerQueries
{
    Task<ScreenerView> GetAsync(AssetClass assetClass = AssetClass.Equity, int topCount = 15, int bottomCount = 10, CancellationToken ct = default);

    /// <summary>
    /// The dashboard highlight: today's consolidated "where to invest" recommendation (one LLM call
    /// across all asset classes). Null until the first run of the day completes.
    /// </summary>
    Task<DailyRecommendationView?> GetDailyRecommendationAsync(CancellationToken ct = default);

    /// <summary>
    /// Crude score-vs-return check: did the names that scored highest at the earliest snapshot
    /// outperform the universe average by the latest snapshot? Null until ≥2 priced snapshots exist.
    /// </summary>
    Task<ScreenerValidation?> GetValidationAsync(CancellationToken ct = default);
}

public sealed record ScreenerView(
    int UniverseSize,
    DateTime? DataAsOfUtc,
    decimal? UniverseMedianPe,
    IReadOnlyList<ScreenerEntry> Opportunities,
    IReadOnlyList<ScreenerEntry> Risks);

public sealed record ScreenerEntry(
    int Rank,
    StockScore Score,
    StockAnalysisView? Analysis);

public sealed record DailyRecommendationView(
    DateTime GeneratedAtUtc,
    string Summary,
    string Caution,
    IReadOnlyList<RecommendationPick> Stocks,
    IReadOnlyList<RecommendationPick> Etfs,
    IReadOnlyList<RecommendationPick> Crypto);

public sealed record RecommendationPick(string Ticker, string Name, string Reason, decimal? PriceAtRecommendation = null);

public sealed record StockAnalysisView(
    DateTime GeneratedAtUtc,
    string Summary,
    string Thesis,
    IReadOnlyList<string> BullishFactors,
    IReadOnlyList<string> BearishFactors,
    IReadOnlyList<string> KeyRisks,
    int Conviction,
    string ConvictionLabel);

public sealed record ScreenerValidation(
    DateTime FromUtc,
    DateTime ToUtc,
    int SampleSize,
    int TopGroupSize,
    decimal TopGroupReturnPct,
    decimal UniverseReturnPct);
