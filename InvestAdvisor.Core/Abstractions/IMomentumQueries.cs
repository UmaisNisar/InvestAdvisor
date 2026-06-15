using InvestAdvisor.Core.Momentum;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>Read model for the Momentum page: today's breakout candidates and the backtest gate.</summary>
public interface IMomentumQueries
{
    Task<MomentumDashboard> GetDashboardAsync(CancellationToken ct = default);
}

/// <summary>
/// Everything the Momentum page renders. <see cref="Validated"/> reflects the gate — when false, the
/// page shows candidates as "paper only, not yet validated" (profit factor ≥1.3 over a meaningful
/// sample, stricter than swing).
/// </summary>
public sealed record MomentumDashboard(
    int UniverseSize,
    MomentumRiskLevel RiskLevel,
    DateTime? GeneratedAtUtc,
    bool Validated,
    IReadOnlyList<MomentumSetupView> Setups,
    MomentumBacktestView? Backtest);

public sealed record MomentumSetupView(
    string Ticker,
    string Name,
    decimal EntryLow,
    decimal EntryHigh,
    decimal StopLoss,
    decimal Target,
    decimal RewardRiskRatio,
    int HoldingDays,
    decimal PositionSizePct,
    decimal TargetGainPct,
    decimal CompositeScore,
    MomentumSetupKind Kind,
    string Rationale,
    decimal? AtrPercent,
    decimal? BreakoutStrength,
    decimal? RelativeVolume,
    DateTime GeneratedAtUtc);

public sealed record MomentumBacktestView(
    DateTime GeneratedAtUtc,
    int TotalTrades,
    decimal WinRatePct,
    decimal ExpectancyR,
    decimal ProfitFactor,
    decimal MaxDrawdownR,
    decimal AverageHoldingDays,
    DateTime? FromUtc,
    DateTime? ToUtc,
    bool HasEdge);
