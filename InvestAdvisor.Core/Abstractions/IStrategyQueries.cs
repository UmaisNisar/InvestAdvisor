using InvestAdvisor.Core.Trading;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>Read models for a strategy page: today's setups, the watchlist, the track record, the gate.</summary>
public interface IStrategyQueries
{
    Task<StrategyDashboard> GetDashboardAsync(StrategyKind kind, CancellationToken ct = default);
}

/// <summary>
/// Everything a strategy page renders. <see cref="Validated"/> reflects the gate — when false, the
/// page shows setups as "paper only, not yet validated". <see cref="Watchlist"/> shows near-setups
/// (empty for strategies without an "almost" state) so the page is useful when nothing qualifies.
/// </summary>
public sealed record StrategyDashboard(
    StrategyKind Kind,
    int UniverseSize,
    RiskLevel RiskLevel,
    DateTime? SetupsGeneratedAtUtc,
    bool Validated,
    IReadOnlyList<SetupView> Setups,
    IReadOnlyList<WatchView> Watchlist,
    TrackRecordView? TrackRecord,
    BacktestView? Backtest);

public sealed record SetupView(
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
    SetupKind Kind,
    string Rationale,
    DateTime GeneratedAtUtc);

public sealed record WatchView(
    string Ticker,
    string Name,
    decimal Close,
    decimal? Rsi,
    decimal? RegimeDistancePct,
    string Note);

/// <summary>Out-of-sample performance from resolved paper trades — the honest scoreboard.</summary>
public sealed record TrackRecordView(
    int Resolved,
    int Open,
    int Wins,
    int Losses,
    decimal WinRatePct,
    decimal TotalR,
    decimal AverageR);

public sealed record BacktestView(
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
