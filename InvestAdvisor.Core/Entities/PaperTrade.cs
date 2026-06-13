namespace InvestAdvisor.Core.Entities;

/// <summary>How an open paper trade ended.</summary>
public enum PaperTradeStatus
{
    Open = 0,
    HitTarget = 1,
    HitStop = 2,
    TimeExit = 3,
}

/// <summary>
/// A swing setup the scanner generated, logged as a paper trade so its real out-of-sample outcome
/// can be measured before any live money is risked. The most recent open rows are "today's setups";
/// resolved rows form the track record. Universe-wide (not per-tenant) — it derives from public
/// market data identical for every user.
/// </summary>
public class PaperTrade
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }

    // The plan, captured at generation so resolution and display use the exact prices suggested.
    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    /// <summary>Mid of the entry zone — the fill price used for R / outcome math.</summary>
    public decimal EntryReference { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RewardRiskRatio { get; set; }
    public int HoldingDays { get; set; }
    public decimal PositionSizePct { get; set; }
    public decimal CompositeScore { get; set; }
    public string Rationale { get; set; } = string.Empty;

    // Signal context at entry — kept so each resolved trade is a labelled example of which
    // conditions did/didn't pay off, the dataset for tuning the strategy over time.
    public decimal? SignalRsi { get; set; }
    public decimal? RegimeDistancePct { get; set; }
    public decimal? PullbackPct { get; set; }
    public decimal? RelativeVolume { get; set; }

    // Outcome, filled in once later bars resolve the trade.
    public PaperTradeStatus Status { get; set; } = PaperTradeStatus.Open;
    public DateTime? ResolvedAtUtc { get; set; }
    public decimal? ExitPrice { get; set; }

    /// <summary>Realized result in R (multiples of the per-trade risk). Null while open.</summary>
    public decimal? RealizedR { get; set; }
}
