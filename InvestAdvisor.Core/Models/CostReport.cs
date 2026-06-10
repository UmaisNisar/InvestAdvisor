namespace InvestAdvisor.Core.Models;

/// <summary>One aggregated spend bucket: a label, how many runs landed in it, the token totals,
/// and the estimated USD. Used for the by-source / by-trigger / by-model breakdowns.</summary>
public sealed record CostLine(string Label, int Runs, long InputTokens, long OutputTokens, decimal Usd);

/// <summary>Per-day spend total (run count + USD) for the trend list.</summary>
public sealed record CostDay(DateTime Date, int Runs, decimal Usd);

/// <summary>
/// Cost history derived entirely from the persisted run rows (AdviceLog / DailyRecommendation /
/// StockAnalysis): no separate cost table is needed since every run already stores model + tokens.
/// </summary>
public sealed record CostReport(
    decimal TodayUsd,
    decimal Last7DaysUsd,
    decimal WindowUsd,
    int TodayRuns,
    int Last7DaysRuns,
    int WindowDays,
    decimal DailyBudgetUsd,
    IReadOnlyList<CostLine> BySource,
    IReadOnlyList<CostLine> ByTrigger,
    IReadOnlyList<CostLine> ByModel,
    IReadOnlyList<CostDay> Daily);
