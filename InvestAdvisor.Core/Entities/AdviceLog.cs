using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Entities;

public class AdviceLog
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public RunTriggerKind Trigger { get; set; }
    public string TriggerDetail { get; set; } = string.Empty;

    public string StructuredInputJson { get; set; } = "{}";
    public string SystemPromptUsed { get; set; } = string.Empty;
    public string RawResponseText { get; set; } = string.Empty;

    public string ParsedSummary { get; set; } = string.Empty;
    public string ParsedFlagsJson { get; set; } = "[]";
    public string ParsedDriftAlertsJson { get; set; } = "[]";
    public string ParsedConsiderationsJson { get; set; } = "[]";
    public string ParsedPositionsJson { get; set; } = "[]";

    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
    public bool ParseFallbackUsed { get; set; }

    public long? ReplayOfAdviceLogId { get; set; }
}
