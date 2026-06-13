namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A near-setup: a name in a confirmed up-trend that hasn't pulled back far enough to trigger yet.
/// Refreshed each scan so the Swing page is never blank — it always shows what to watch and what
/// would make it fire, even on days with no actionable setup. Universe-wide, not per-tenant.
/// </summary>
public class SwingWatchItem
{
    public long Id { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Close { get; set; }
    public decimal CompositeScore { get; set; }
    public decimal? Rsi { get; set; }
    public decimal? RegimeDistancePct { get; set; }
    public decimal? TrendDistancePct { get; set; }

    /// <summary>Short human note on what it would take to trigger.</summary>
    public string Note { get; set; } = string.Empty;
}
