using InvestAdvisor.Core.Swing;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>Read models for the Swing page: today's setups, the watchlist, the track record, the gate.</summary>
public interface ISwingQueries
{
    Task<SwingDashboard> GetDashboardAsync(CancellationToken ct = default);
}

/// <summary>
/// Everything the Swing page renders. <see cref="Validated"/> reflects the gate — when false, the
/// page shows setups as "paper only, not yet validated". <see cref="Watchlist"/> shows near-setups
/// so the page is useful even when nothing qualifies.
/// </summary>
public sealed record SwingDashboard(
    int UniverseSize,
    SwingRiskLevel RiskLevel,
    DateTime? SetupsGeneratedAtUtc,
    bool Validated,
    IReadOnlyList<SwingSetupView> Setups,
    IReadOnlyList<SwingWatchView> Watchlist,
    SwingTrackRecord? TrackRecord,
    SwingBacktestView? Backtest);

public sealed record SwingWatchView(
    string Ticker,
    string Name,
    decimal Close,
    decimal? Rsi,
    decimal? RegimeDistancePct,
    string Note);

public sealed record SwingSetupView(
    string Ticker,
    string Name,
    decimal EntryLow,
    decimal EntryHigh,
    decimal StopLoss,
    decimal Target,
    decimal RewardRiskRatio,
    int HoldingDays,
    decimal PositionSizePct,
    decimal CompositeScore,
    SwingSetupKind Kind,
    string Rationale,
    DateTime GeneratedAtUtc);

/// <summary>Out-of-sample performance from resolved paper trades — the honest scoreboard.</summary>
public sealed record SwingTrackRecord(
    int Resolved,
    int Open,
    int Wins,
    int Losses,
    decimal WinRatePct,
    decimal TotalR,
    decimal AverageR);

public sealed record SwingBacktestView(
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
