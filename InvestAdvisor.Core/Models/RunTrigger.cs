using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Models;

/// <summary>
/// Why a run fired. <paramref name="Ticker"/> is set for condition triggers (PriceTarget,
/// BigMove, DriftThreshold) so the context assembler can focus the payload on the affected
/// name; null for Manual/Scheduled runs.
/// </summary>
public sealed record RunTrigger(RunTriggerKind Kind, string Detail, string? Ticker = null);
