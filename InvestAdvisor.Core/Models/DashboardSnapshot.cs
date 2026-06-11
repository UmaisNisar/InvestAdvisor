namespace InvestAdvisor.Core.Models;

/// <summary>
/// Composed read returned by IPortfolioQueries for the Dashboard page. Money values are USD;
/// <paramref name="RatesToUsd"/> (multiplier currency→USD, always includes USD and
/// <paramref name="DisplayCurrency"/>) lets the UI re-denominate them for display.
/// </summary>
public sealed record DashboardSnapshot(
    PortfolioTotals Totals,
    IReadOnlyList<HoldingView> Holdings,
    AllocationView Allocation,
    IReadOnlyList<MoverView> TopMovers,
    LatestAdviceSummary? LatestAdvice,
    IReadOnlyDictionary<string, decimal> RatesToUsd,
    string DisplayCurrency);

public sealed record LatestAdviceSummary(
    long AdviceLogId,
    DateTime TimestampUtc,
    string Trigger,
    string TriggerDetail,
    string Summary,
    int FlagCount,
    int DriftAlertCount,
    IReadOnlyList<PositionCall> Positions);
