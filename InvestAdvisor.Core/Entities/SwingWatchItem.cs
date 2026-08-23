using InvestAdvisor.Core.Trading;

namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A near-setup: a scored name that hasn't triggered yet (for swing: in a confirmed up-trend but
/// not pulled back far enough). Refreshed each scan so the strategy page is never blank — it always
/// shows what to watch and what would make it fire. Universe-wide, not per-tenant.
/// </summary>
public class SwingWatchItem
{
    public long Id { get; set; }
    public StrategyKind Strategy { get; set; }
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
