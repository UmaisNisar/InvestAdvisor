namespace InvestAdvisor.Core.Entities;

/// <summary>
/// LLM-generated per-stock analysis (Phase 3), stored alongside the rule-based
/// <see cref="CompositeScore"/> that selected this stock for deep analysis.
/// Conviction is a qualitative read, not investment advice.
/// </summary>
public class StockAnalysis
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Rule-based rank score (Phase 2) that put this stock on the shortlist.</summary>
    public decimal CompositeScore { get; set; }

    public string Summary { get; set; } = string.Empty;
    public string Thesis { get; set; } = string.Empty;
    public string BullishFactorsJson { get; set; } = "[]";
    public string BearishFactorsJson { get; set; } = "[]";
    public string KeyRisksJson { get; set; } = "[]";

    /// <summary>0–100 qualitative conviction. Surfaced with a "not advice" caveat.</summary>
    public int Conviction { get; set; }
    public string ConvictionLabel { get; set; } = string.Empty; // low | medium | high

    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
}
