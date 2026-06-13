using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Swing;

/// <summary>
/// Tunable parameters for the swing engine. The strategy is <b>regime-filtered short-term mean
/// reversion</b>: only buy names in a confirmed long-term up-trend (above the 200-day SMA), and only
/// when they've pulled back to a short-term oversold extreme (low RSI(3)) — then ride the bounce for
/// 2–3 days with a wide ATR stop. This is the opposite of momentum-chasing, which doesn't pay on a
/// 2–3 day hold. The same instance drives live scoring and the backtest, so what you validate is
/// what you trade.
/// </summary>
public sealed record SwingParams
{
    /// <summary>Long-term trend filter: only go long when close is above this SMA.</summary>
    public int RegimeSmaPeriod { get; init; } = 200;

    /// <summary>Intermediate SMA used to gauge how far price has pulled back short-term.</summary>
    public int PullbackSmaPeriod { get; init; } = 5;

    /// <summary>Short RSI for the oversold trigger — Connors-style. 2–3 suits a 2–3 day hold.</summary>
    public int RsiPeriod { get; init; } = 3;

    /// <summary>Enter only when RSI(<see cref="RsiPeriod"/>) is at or below this (oversold pullback).</summary>
    public decimal OversoldEntry { get; init; } = 20m;

    public int AtrPeriod { get; init; } = 14;
    public int VolumeLookback { get; init; } = 20;

    /// <summary>Stop distance = this × ATR below entry. Wide, because mean reversion catches a dip.</summary>
    public decimal AtrStopMultiple { get; init; } = 2.5m;

    /// <summary>Bounce target = this × ATR above entry. Modest, so it's reachable inside the hold.</summary>
    public decimal TargetAtrMultiple { get; init; } = 1.5m;

    /// <summary>Planned holding period in trading sessions.</summary>
    public int HoldingDays { get; init; } = 3;

    /// <summary>Capital risked per trade, as a percent — the numerator of position sizing.</summary>
    public decimal RiskPerTradePct { get; init; } = 1.0m;

    /// <summary>Hard cap on any single position, as a percent of capital.</summary>
    public decimal MaxPositionPct { get; init; } = 25m;

    /// <summary>Liquidity floor: trailing avg dollar volume below this excludes the name entirely.</summary>
    public decimal MinAvgDollarVolume { get; init; } = 5_000_000m;

    /// <summary>Reward:risk implied by the ATR multiples (for display).</summary>
    public decimal RewardRiskRatio => AtrStopMultiple == 0m ? 0m : TargetAtrMultiple / AtrStopMultiple;

    public static readonly SwingParams Default = new();
}

/// <summary>One stock's bars plus identity, fed to the swing scorer. Candles are oldest-first.</summary>
public sealed record SwingInput(
    string Ticker,
    string Name,
    string Sector,
    AssetClass AssetClass,
    IReadOnlyList<Candle> Candles);

/// <summary>The raw indicator readings behind a swing score, surfaced for display and outcome tracking.</summary>
public sealed record SwingFeatures(
    decimal Close,
    decimal? RegimeSma,
    decimal? Rsi,
    decimal? PullbackPct,
    decimal? RelativeVolume,
    decimal? Atr,
    decimal? AverageDollarVolume)
{
    /// <summary>In a confirmed long-term up-trend (the only regime we go long in).</summary>
    public bool AboveRegime => RegimeSma is { } s && s > 0m && Close > s;

    /// <summary>How far above the 200-day SMA, as a fraction — uptrend health (and over-extension).</summary>
    public decimal? RegimeDistancePct => RegimeSma is { } s && s > 0m ? (Close - s) / s : null;
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

/// <summary>The mean-reversion sub-scores (0–100 universe percentiles), null where data is missing.</summary>
public sealed record SwingFactorScores(
    decimal? Regime,
    decimal? Oversold,
    decimal? Pullback,
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
