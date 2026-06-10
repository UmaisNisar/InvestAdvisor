namespace InvestAdvisor.Core.Models;

/// <summary>
/// The structured payload sent to the LLM each run. All arithmetic is computed in C#
/// by the <see cref="Agent.ContextAssembler"/> before serialization — the model never
/// does math on raw prices.
/// </summary>
public sealed record RunContext(
    DateTime GeneratedAtUtc,
    string TriggerKind,
    string TriggerDetail,
    ProfileSnapshot Profile,
    PortfolioTotals Totals,
    IReadOnlyList<HoldingView> Holdings,
    AllocationView Allocation,
    IReadOnlyList<MoverView> TopMovers,
    IReadOnlyList<NewsHeadline> RecentNews,
    IReadOnlyList<TickerSentimentView> Sentiment,
    IReadOnlyList<string>? DataCaveats = null);

public sealed record ProfileSnapshot(
    string GoalsText,
    string RiskTolerance,
    string TimeHorizon,
    decimal DriftPctThreshold,
    decimal SingleDayMovePctThreshold,
    int RebalanceCadenceHours);

public sealed record PortfolioTotals(
    decimal MarketValueUsd,
    decimal CostBasisUsd,
    decimal UnrealizedPnlUsd,
    decimal UnrealizedPnlPct,
    decimal TodaysChangeUsd,
    decimal TodaysChangePct);

public sealed record HoldingView(
    string Ticker,
    string Name,
    string AssetClass,
    string AccountType,
    decimal Quantity,
    decimal AvgCost,
    decimal? Price,
    decimal? MarketValueUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPct,
    decimal? TodaysChangePct,
    decimal? CurrentAllocationPct,
    decimal? TargetAllocationPct,
    decimal? DriftPct,
    string Currency = "USD",
    DateTime? PriceAsOfUtc = null,
    bool PriceIsStale = false,
    decimal? MomentumShortPct = null,
    decimal? MomentumLongPct = null);

public sealed record AllocationView(
    IReadOnlyDictionary<string, decimal> ByAssetClass,
    IReadOnlyDictionary<string, decimal> ByAccountType,
    IReadOnlyList<DriftRow> Drifts);

public sealed record DriftRow(string Ticker, decimal CurrentPct, decimal TargetPct, decimal DriftPct);

public sealed record MoverView(string Ticker, decimal Price, decimal PercentChange, string Direction);
