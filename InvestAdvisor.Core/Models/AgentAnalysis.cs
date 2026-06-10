using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Models;

/// <summary>
/// Parsed result returned by the LLM after a successful tool_use of <c>emit_analysis</c>.
/// </summary>
public sealed record AgentAnalysis(
    string Summary,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<DriftAlert> DriftAlerts,
    IReadOnlyList<Consideration> Considerations,
    AgentRunMetrics Metrics,
    IReadOnlyList<PositionCall> Positions);

/// <summary>Per-holding stance (add / hold / trim / sell) with a conviction and a data-grounded reason.</summary>
public sealed record PositionCall(
    string Ticker,
    PositionStance Stance,
    PositionConviction Conviction,
    string Reason,
    bool KnownTicker = true);

public sealed record Flag(
    FlagSeverity Severity,
    string? Ticker,
    string Title,
    string Detail,
    IReadOnlyList<string>? Evidence,
    bool KnownTicker = true);

public sealed record DriftAlert(
    DriftSeverity Severity,
    string Ticker,
    decimal CurrentPct,
    decimal TargetPct,
    decimal DriftPct,
    string Note,
    bool KnownTicker = true);

public sealed record Consideration(string Topic, string Text);

public sealed record AgentRunMetrics(
    string Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    bool ParseFallbackUsed);
