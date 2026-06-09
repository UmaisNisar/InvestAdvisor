namespace InvestAdvisor.Core.Models;

/// <summary>Composed read returned by IPortfolioQueries for the Dashboard page.</summary>
public sealed record DashboardSnapshot(
    PortfolioTotals Totals,
    IReadOnlyList<HoldingView> Holdings,
    AllocationView Allocation,
    IReadOnlyList<MoverView> TopMovers,
    LatestAdviceSummary? LatestAdvice);

public sealed record LatestAdviceSummary(
    long AdviceLogId,
    DateTime TimestampUtc,
    string Trigger,
    string TriggerDetail,
    string Summary,
    int FlagCount,
    int DriftAlertCount,
    IReadOnlyList<PositionCall> Positions);
