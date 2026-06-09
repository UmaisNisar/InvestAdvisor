using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Models;

public sealed record AdviceLogSummaryView(
    long Id,
    DateTime TimestampUtc,
    RunTriggerKind Trigger,
    string TriggerDetail,
    string Summary,
    int FlagCount,
    int CriticalFlagCount,
    int WarnFlagCount,
    int DriftAlertCount,
    int ActionSuggestedDriftCount,
    long? ReplayOfAdviceLogId);

public sealed record AdviceLogDetailView(
    long Id,
    DateTime TimestampUtc,
    RunTriggerKind Trigger,
    string TriggerDetail,
    string Summary,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<DriftAlert> DriftAlerts,
    IReadOnlyList<Consideration> Considerations,
    IReadOnlyList<PositionCall> Positions,
    string SystemPromptUsed,
    string StructuredInputJson,
    string RawResponseText,
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed,
    long? ReplayOfAdviceLogId);

public sealed record AdvicePage(IReadOnlyList<AdviceLogSummaryView> Items, int TotalCount);

public sealed record HealthStatus(
    bool DatabaseOk,
    DateTime? LastFinnhubFetchUtc,
    DateTime? LastAnthropicCallUtc,
    int TotalAdviceLogs,
    int TotalHoldings);
