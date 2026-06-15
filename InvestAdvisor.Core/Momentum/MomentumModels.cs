using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Momentum;

/// <summary>
/// How aggressively the high-volatility engine surfaces breakout setups — the user-facing risk dial.
/// Higher risk loosens the base/volume quality bar, tightens the stop (so it sits closer and is hit
/// more often, but each loss is a smaller fraction of the bigger reward), sizes positions larger, and
/// surfaces more names. It is a genuine quality/quantity/exposure trade-off, not a free lunch.
/// </summary>
public enum MomentumRiskLevel
{
    /// <summary>Strict: only the tightest coils with heavy volume confirmation, small size, few names.</summary>
    Low = 0,

    /// <summary>Balanced: moderate base tightness, continuation setups on, standard sizing.</summary>
    Medium = 1,

    /// <summary>Aggressive: looser bases qualify, tight stops, large size, many names. More setups, more noise.</summary>
    High = 2,
}

/// <summary>
/// Tunable parameters for the high-volatility engine. The strategy is <b>volatility-expansion
/// breakout</b>: among names volatile enough that a ~10% move in 3 sessions is plausible, buy the
/// break of a tight consolidation on a volume surge, riding the expansion for a few days with a tight
/// ATR stop and an asymmetric (reward &gt; risk) target. This is momentum, the opposite of the
/// mean-reversion swing engine. The same instance drives live scoring and the backtest, so what you
/// validate is what you trade.
/// </summary>
public sealed record MomentumParams
{
    /// <summary>Trend bias: only go long when close is above this SMA (don't fight the intermediate trend).</summary>
    public int TrendSmaPeriod { get; init; } = 50;

    /// <summary>Lookback whose prior high the close must break to count as a breakout.</summary>
    public int BreakoutLookback { get; init; } = 20;

    /// <summary>Window used to measure the pre-breakout consolidation base's tightness.</summary>
    public int BasePeriod { get; init; } = 10;

    public int AtrPeriod { get; init; } = 14;
    public int VolumeLookback { get; init; } = 20;
    public int RsiPeriod { get; init; } = 14;
    public int MomentumLookback { get; init; } = 20;

    /// <summary>Volatility floor: ATR as a fraction of price must be at least this — keeps a 10% target reachable.</summary>
    public decimal MinAtrPercent { get; init; } = 0.035m;

    /// <summary>
    /// Base must be no wider than this many ATRs to count as a tight "squeeze". Measured in ATR units
    /// (not a fixed % of price) so the bar scales with the name's own volatility — a 6%-ATR mover and a
    /// 2%-ATR name are judged on the same "how coiled is it, for *this* stock" basis.
    /// </summary>
    public decimal MaxBaseRangeAtr { get; init; } = 3.5m;

    /// <summary>Close must clear the prior high by at least this fraction to confirm the breakout.</summary>
    public decimal BreakoutMargin { get; init; } = 0m;

    /// <summary>Volume surge: latest volume must be at least this multiple of its trailing average.</summary>
    public decimal MinRelativeVolume { get; init; } = 1.5m;

    /// <summary>Whether the gentler "strong momentum continuation" trigger (no tight base required) is active.</summary>
    public bool EnableContinuation { get; init; } = true;

    /// <summary>Continuation: trailing momentum (return over <see cref="MomentumLookback"/>) must exceed this.</summary>
    public decimal MinContinuationReturn { get; init; } = 0.15m;

    /// <summary>Stop distance = this × ATR below entry. Tight, because a real breakout shouldn't come back.</summary>
    public decimal StopAtrMultiple { get; init; } = 1.25m;

    /// <summary>Target = this × ATR above entry. Wide and asymmetric — the move we're paying for.</summary>
    public decimal TargetAtrMultiple { get; init; } = 2.5m;

    /// <summary>Planned holding period in trading sessions — also the hard max-hold cap in the backtest.</summary>
    public int HoldingDays { get; init; } = 3;

    /// <summary>
    /// Exit model. When false (default) the trade exits at the fixed ATR target, the stop, or the
    /// holding-window close. When true, the fixed take-profit is removed and a trailing stop runs
    /// instead: the stop ratchets up to <see cref="TrailAtrMultiple"/>×ATR below the running high once
    /// the move is at least <see cref="TrailActivateR"/>×R in profit — letting winners run (the
    /// right-skew momentum needs) and capping losers at the initial stop.
    /// </summary>
    public bool UseTrailingStop { get; init; }

    /// <summary>Trailing stop kicks in once unrealized profit reaches this many R (breakeven-and-trail).</summary>
    public decimal TrailActivateR { get; init; } = 1.0m;

    /// <summary>Once active, the trailing stop sits this many ATR below the highest high since entry.</summary>
    public decimal TrailAtrMultiple { get; init; } = 2.5m;

    /// <summary>Capital risked per trade, as a percent — the numerator of position sizing.</summary>
    public decimal RiskPerTradePct { get; init; } = 1.5m;

    /// <summary>Hard cap on any single position, as a percent of capital.</summary>
    public decimal MaxPositionPct { get; init; } = 30m;

    /// <summary>How many top qualifying setups to surface (and paper-trade) per scan.</summary>
    public int SetupCount { get; init; } = 5;

    /// <summary>
    /// Liquidity floor: trailing avg dollar volume below this excludes the name. Lower than the swing
    /// engine's — the high-vol pool is smaller-cap — but high enough to fill a small position cleanly.
    /// </summary>
    public decimal MinAvgDollarVolume { get; init; } = 1_000_000m;

    /// <summary>Reward:risk implied by the ATR multiples (for display).</summary>
    public decimal RewardRiskRatio => StopAtrMultiple == 0m ? 0m : TargetAtrMultiple / StopAtrMultiple;

    public static readonly MomentumParams Default = For(MomentumRiskLevel.Medium);

    /// <summary>
    /// Preset for a risk level. The dial moves quality (base tightness + volume confirmation), the
    /// stop multiple, per-trade capital at risk, and how many names surface. Lower = stricter/smaller;
    /// higher = looser/larger. High targets a ~10% move (≈2.5×ATR on a ≥4%-ATR name).
    /// </summary>
    public static MomentumParams For(MomentumRiskLevel level) => level switch
    {
        MomentumRiskLevel.Low => new MomentumParams
        {
            MinAtrPercent = 0.035m, MaxBaseRangeAtr = 2.5m, MinRelativeVolume = 2.0m,
            EnableContinuation = false, StopAtrMultiple = 1.5m, TargetAtrMultiple = 3.0m,
            RiskPerTradePct = 0.75m, MaxPositionPct = 20m, SetupCount = 3,
        },
        MomentumRiskLevel.High => new MomentumParams
        {
            MinAtrPercent = 0.04m, MaxBaseRangeAtr = 4.5m, MinRelativeVolume = 1.3m,
            EnableContinuation = true, StopAtrMultiple = 1.0m, TargetAtrMultiple = 2.5m,
            RiskPerTradePct = 3.0m, MaxPositionPct = 40m, SetupCount = 6,
        },
        _ => new MomentumParams
        {
            MinAtrPercent = 0.035m, MaxBaseRangeAtr = 3.5m, MinRelativeVolume = 1.5m,
            EnableContinuation = true, StopAtrMultiple = 1.25m, TargetAtrMultiple = 2.5m,
            RiskPerTradePct = 1.5m, MaxPositionPct = 30m, SetupCount = 5,
        },
    };
}

/// <summary>One stock's bars plus identity, fed to the momentum scorer. Candles are oldest-first.</summary>
public sealed record MomentumInput(
    string Ticker,
    string Name,
    string Sector,
    AssetClass AssetClass,
    IReadOnlyList<Candle> Candles);

/// <summary>The raw indicator readings behind a momentum score, surfaced for display and outcome tracking.</summary>
public sealed record MomentumFeatures(
    decimal Close,
    decimal? TrendSma,
    decimal? Atr,
    decimal? AtrPercent,
    decimal? BreakoutStrength,
    decimal? BaseRangePct,
    decimal? RelativeVolume,
    decimal? Rsi,
    decimal? MomentumReturn,
    decimal? AverageDollarVolume)
{
    /// <summary>Above the intermediate trend — the only side we take.</summary>
    public bool AboveTrend => TrendSma is { } s && s > 0m && Close > s;

    /// <summary>Volatile enough that a double-digit move in the hold window is plausible.</summary>
    public bool IsHighVolatility(MomentumParams p) => AtrPercent is { } a && a >= p.MinAtrPercent;

    /// <summary>
    /// Consolidation base width expressed in ATRs (range ÷ ATR) — tightness that scales with the name's
    /// own volatility, so a high-beta mover isn't unfairly judged "loose" just for being volatile.
    /// </summary>
    public decimal? BaseRangeAtr => BaseRangePct is { } r && Atr is { } a && a > 0m ? r * Close / a : null;
}

/// <summary>
/// Which trigger fired — and the implied conviction. A squeeze breakout (tight base + volume surge)
/// is the higher-conviction setup; a strong momentum continuation with no tight base is the gentler
/// "B" setup that only the looser risk levels surface.
/// </summary>
public enum MomentumSetupKind
{
    None = 0,
    /// <summary>Break of a tight consolidation on a volume surge — the stronger signal.</summary>
    SqueezeBreakout = 1,
    /// <summary>Strong trailing momentum carrying through a breakout — gentler, lower conviction.</summary>
    Continuation = 2,
}

/// <summary>
/// A concrete, risk-bounded breakout plan. Never a bare "buy" — every setup carries the stop and the
/// size, so the worst case is defined before entry. All prices in the instrument's own currency.
/// </summary>
public sealed record MomentumSetup(
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
    MomentumSetupKind Kind,
    string Rationale)
{
    /// <summary>Reference entry (mid of the zone) used for risk math and backtest fills.</summary>
    public decimal EntryReference => (EntryLow + EntryHigh) / 2m;
    public decimal StopDistancePct => EntryReference == 0m ? 0m : (EntryReference - StopLoss) / EntryReference * 100m;
    /// <summary>Projected gain to target, as a percent — the headline "~10%" number.</summary>
    public decimal TargetGainPct => EntryReference == 0m ? 0m : (Target - EntryReference) / EntryReference * 100m;
}

/// <summary>The momentum sub-scores (0–100 universe percentiles), null where data is missing.</summary>
public sealed record MomentumFactorScores(
    decimal? Breakout,
    decimal? Squeeze,
    decimal? Volume,
    decimal? Volatility,
    decimal? Momentum,
    decimal? Sentiment);

/// <summary>One ranked momentum candidate: composite score, sub-scores, the trade plan, and raw inputs.</summary>
public sealed record MomentumScore(
    string Ticker,
    string Name,
    string Sector,
    decimal CompositeScore,
    MomentumFactorScores Factors,
    MomentumFeatures Features,
    MomentumSetup Setup,
    bool Qualifies);
