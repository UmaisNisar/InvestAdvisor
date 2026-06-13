using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>
/// Tunable parameters for the swing engine. Defaults target a 2–3 day hold on liquid names with a
/// 1.5×ATR stop and a 1.5:1 reward:risk target. The same instance drives both live scoring and the
/// backtest so what you validate is what you trade.
/// </summary>
public sealed record SwingParams
{
    public int RsiPeriod { get; init; } = 14;
    public int EmaFastPeriod { get; init; } = 9;
    public int EmaSlowPeriod { get; init; } = 21;
    public int AtrPeriod { get; init; } = 14;
    public int BreakoutLookback { get; init; } = 20;
    public int VolumeLookback { get; init; } = 20;
    public int MomentumSessions { get; init; } = 5;

    /// <summary>Stop distance = this × ATR below the entry reference.</summary>
    public decimal AtrStopMultiple { get; init; } = 1.5m;

    /// <summary>Target distance = this × the stop distance (the reward:risk ratio).</summary>
    public decimal RewardRiskRatio { get; init; } = 1.5m;

    /// <summary>Planned holding period in trading sessions.</summary>
    public int HoldingDays { get; init; } = 3;

    /// <summary>Capital risked per trade, as a percent — the numerator of position sizing.</summary>
    public decimal RiskPerTradePct { get; init; } = 1.0m;

    /// <summary>Hard cap on any single position, as a percent of capital.</summary>
    public decimal MaxPositionPct { get; init; } = 25m;

    /// <summary>Liquidity floor: trailing avg dollar volume below this excludes the name entirely.</summary>
    public decimal MinAvgDollarVolume { get; init; } = 5_000_000m;

    public static readonly SwingParams Default = new();
}

/// <summary>One stock's bars plus identity, fed to the swing scorer. Candles are oldest-first.</summary>
public sealed record SwingInput(
    string Ticker,
    string Name,
    string Sector,
    AssetClass AssetClass,
    IReadOnlyList<Candle> Candles);

/// <summary>The raw indicator readings behind a swing score, surfaced for display and the LLM.</summary>
public sealed record SwingFeatures(
    decimal Close,
    decimal? Rsi,
    decimal? EmaFast,
    decimal? EmaSlow,
    decimal? BreakoutStrength,
    decimal? RelativeVolume,
    decimal? Momentum,
    decimal? Gap,
    decimal? Atr,
    decimal? AverageDollarVolume)
{
    /// <summary>Fast EMA above slow EMA — the basic up-trend filter.</summary>
    public bool TrendUp => EmaFast is { } f && EmaSlow is { } s && f > s;
}

/// <summary>
/// A concrete, risk-bounded trade plan. Never a bare "buy" — every setup carries the stop and the
/// size, so the worst case is defined before entry. All prices in the instrument's own currency.
/// </summary>
public sealed record TradeSetup(
    string Ticker,
    string Name,
    DateTime AsOfUtc,
    decimal EntryLow,
    decimal EntryHigh,
    decimal StopLoss,
    decimal Target,
    decimal RewardRiskRatio,
    int HoldingDays,
    decimal PositionSizePct,
    string Rationale)
{
    /// <summary>Reference entry (mid of the zone) used for risk math and backtest fills.</summary>
    public decimal EntryReference => (EntryLow + EntryHigh) / 2m;
    public decimal StopDistancePct => EntryReference == 0m ? 0m : (EntryReference - StopLoss) / EntryReference * 100m;
}

/// <summary>The seven swing sub-scores (0–100 universe percentiles), null where data is missing.</summary>
public sealed record SwingFactorScores(
    decimal? Trend,
    decimal? Momentum,
    decimal? Breakout,
    decimal? Rsi,
    decimal? Volume,
    decimal? Sentiment);

/// <summary>One ranked swing candidate: composite score, sub-scores, the trade plan, and raw inputs.</summary>
public sealed record SwingScore(
    string Ticker,
    string Name,
    string Sector,
    decimal CompositeScore,
    SwingFactorScores Factors,
    SwingFeatures Features,
    TradeSetup Setup,
    bool Qualifies);
