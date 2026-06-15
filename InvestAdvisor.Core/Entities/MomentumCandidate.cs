using InvestAdvisor.Core.Momentum;

namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A high-vol breakout setup the scanner surfaced on its latest scan, persisted so the Momentum page
/// can render today's ideas without re-fetching bars for the whole universe on every load. Replaced
/// wholesale each scan (the snapshot is always "today's top setups"). Universe-wide, not per-tenant —
/// it derives from public market data identical for every user.
/// </summary>
public class MomentumCandidate
{
    public long Id { get; set; }
    public DateTime GeneratedAtUtc { get; set; }

    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // The risk-bounded plan, captured at scan time.
    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    public decimal EntryReference { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RewardRiskRatio { get; set; }
    public int HoldingDays { get; set; }
    public decimal PositionSizePct { get; set; }
    /// <summary>Projected gain to target, as a percent — the headline "~10%" number.</summary>
    public decimal TargetGainPct { get; set; }
    public decimal CompositeScore { get; set; }

    /// <summary>Which trigger produced this setup — drives the conviction tag on the card.</summary>
    public MomentumSetupKind Kind { get; set; } = MomentumSetupKind.None;
    public string Rationale { get; set; } = string.Empty;

    // Signal context at scan time, for display.
    public decimal? AtrPercent { get; set; }
    public decimal? BreakoutStrength { get; set; }
    public decimal? RelativeVolume { get; set; }
}
