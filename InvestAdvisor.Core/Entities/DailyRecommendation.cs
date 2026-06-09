namespace InvestAdvisor.Core.Entities;

/// <summary>
/// The single daily "where to invest today" recommendation produced by one consolidated LLM call
/// across all asset classes. One row per day. The per-class picks are stored as JSON arrays of
/// {ticker, name, reason}.
/// </summary>
public class DailyRecommendation
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Caution { get; set; } = string.Empty;
    public string StocksJson { get; set; } = "[]";
    public string EtfsJson { get; set; } = "[]";
    public string CryptoJson { get; set; } = "[]";
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
}
