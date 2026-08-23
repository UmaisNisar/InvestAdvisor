using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Trading;

/// <summary>The short-horizon trading strategies the app runs. Persisted as an int discriminator.</summary>
public enum StrategyKind
{
    /// <summary>Regime-filtered mean reversion: buy an up-trend's oversold pullback, ride the 2–3 day bounce.</summary>
    Swing = 0,

    /// <summary>Volatility-expansion breakout: buy a high-vol name breaking a tight base on volume, ride the expansion.</summary>
    Momentum = 1,
}

/// <summary>
/// How aggressively a strategy surfaces setups — the user-facing risk dial. Higher loosens the
/// trigger quality bar, sizes positions larger and surfaces more names; lower is stricter and
/// smaller. A genuine quality/quantity/exposure trade-off, not a free lunch.
/// </summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Which trigger fired — and the implied conviction. Every strategy has a higher-conviction
/// <see cref="Primary"/> trigger and a gentler <see cref="Secondary"/> one that only the looser risk
/// levels enable; the strategy supplies the human labels (e.g. "Oversold · A" / "MA bounce · B").
/// </summary>
public enum SetupKind
{
    None = 0,
    Primary = 1,
    Secondary = 2,
}

/// <summary>
/// The knobs every strategy shares: indicator windows, the ATR-based exit geometry, sizing, the
/// surfacing cap, the liquidity floor, and the backtest's friction/validation constants. Each
/// strategy derives a record adding its own trigger thresholds and the per-risk-level presets.
/// The same instance drives live scoring and the backtest, so what you validate is what you trade.
/// </summary>
public abstract record StrategyParams
{
    public int AtrPeriod { get; init; } = 14;
    public int VolumeLookback { get; init; } = 20;
    public int RsiPeriod { get; init; } = 14;

    /// <summary>Intermediate trend SMA.</summary>
    public int TrendSmaPeriod { get; init; } = 50;

    /// <summary>Stop distance = this × ATR below entry.</summary>
    public decimal StopAtrMultiple { get; init; } = 2.0m;

    /// <summary>Target = this × ATR above entry.</summary>
    public decimal TargetAtrMultiple { get; init; } = 2.0m;

    /// <summary>Planned holding period in trading sessions — also the hard max-hold cap in the backtest.</summary>
    public int HoldingDays { get; init; } = 3;

    /// <summary>Capital risked per trade, as a percent — the numerator of position sizing.</summary>
    public decimal RiskPerTradePct { get; init; } = 1.0m;

    /// <summary>Hard cap on any single position, as a percent of capital.</summary>
    public decimal MaxPositionPct { get; init; } = 25m;

    /// <summary>How many top qualifying setups to surface (and paper-trade) per scan.</summary>
    public int SetupCount { get; init; } = 5;

    /// <summary>Liquidity floor: trailing avg dollar volume below this excludes the name entirely.</summary>
    public decimal MinAvgDollarVolume { get; init; } = 5_000_000m;

    /// <summary>
    /// Exit model. False: exit at the fixed ATR target, the stop, or the holding-window close. True:
    /// no fixed take-profit; a chandelier trail ratchets the stop up to <see cref="TrailAtrMultiple"/>
    /// × ATR below the running high once the move is <see cref="TrailActivateR"/> × R in profit.
    /// </summary>
    public bool UseTrailingStop { get; init; }
    public decimal TrailActivateR { get; init; } = 1.0m;
    public decimal TrailAtrMultiple { get; init; } = 2.5m;

    /// <summary>Backtest round-trip cost (commission + slippage) as a fraction of entry price.</summary>
    public decimal RoundTripCost { get; init; } = 0.001m;

    /// <summary>Backtest validation gate: profit factor the rule must clear to be called an edge.</summary>
    public decimal MinProfitFactor { get; init; } = 1.15m;

    /// <summary>Reward:risk implied by the ATR multiples (for display).</summary>
    public decimal RewardRiskRatio => StopAtrMultiple == 0m ? 0m : TargetAtrMultiple / StopAtrMultiple;
}

/// <summary>One stock's bars plus identity, fed to a strategy. Candles are oldest-first.</summary>
public sealed record StrategyInput(
    string Ticker,
    string Name,
    string Sector,
    AssetClass AssetClass,
    IReadOnlyList<Candle> Candles);

/// <summary>
/// The indicator readings every strategy computes; each strategy derives a record adding its own.
/// <see cref="Atr"/> is non-null by construction — a strategy returns null features instead of a
/// reading it can't size a stop from.
/// </summary>
public abstract record StrategyFeatures(
    decimal Close,
    decimal Atr,
    decimal? TrendSma,
    decimal? Rsi,
    decimal? RelativeVolume,
    decimal? AverageDollarVolume)
{
    /// <summary>Above the intermediate trend — the only side any strategy takes.</summary>
    public bool AboveTrend => TrendSma is { } s && s > 0m && Close > s;

    /// <summary>Distance above the trend SMA, as a fraction (negative = below it). Null if missing.</summary>
    public decimal? TrendDistancePct => TrendSma is { } s && s > 0m ? (Close - s) / s : null;
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
    SetupKind Kind,
    string Rationale)
{
    /// <summary>Reference entry (mid of the zone) used for risk math and backtest fills.</summary>
    public decimal EntryReference => (EntryLow + EntryHigh) / 2m;
    public decimal StopDistancePct => EntryReference == 0m ? 0m : (EntryReference - StopLoss) / EntryReference * 100m;
    /// <summary>Projected gain to target, as a percent.</summary>
    public decimal TargetGainPct => EntryReference == 0m ? 0m : (Target - EntryReference) / EntryReference * 100m;
}

/// <summary>One ranked candidate: composite score, the trade plan, and the raw readings behind it.</summary>
public sealed record StrategyScore(
    string Ticker,
    string Name,
    string Sector,
    decimal CompositeScore,
    StrategyFeatures Features,
    TradeSetup Setup,
    bool Qualifies);

/// <summary>One ranking factor: what to read off the features, which direction is better, and its weight.</summary>
public sealed record FactorSpec(string Name, Func<StrategyFeatures, decimal?> Select, bool HigherBetter, decimal Weight);

/// <summary>
/// What makes one strategy different from another: its parameters per risk level, how it reads
/// bars into features, which readings qualify a long entry, and how it ranks candidates. The
/// shared planner, scorer, backtester, service, queries and page do everything else.
/// </summary>
public interface IStrategy
{
    StrategyKind Kind { get; }

    StrategyParams ParamsFor(RiskLevel level);

    /// <summary>Bars needed before the first signal can form (the longest lookback + 1).</summary>
    int Warmup(StrategyParams p);

    /// <summary>Reads bars into features, or null when there aren't enough bars / no volatility estimate.</summary>
    StrategyFeatures? Read(IReadOnlyList<Candle> candles, StrategyParams p);

    /// <summary>Whether a long entry qualifies on these readings at this risk level.</summary>
    bool Qualifies(StrategyFeatures f, StrategyParams p);

    /// <summary>Which trigger fired (the primary outranks the secondary when both do).</summary>
    SetupKind KindOf(StrategyFeatures f, StrategyParams p);

    /// <summary>One-line human explanation of the readings plus the exit plan.</summary>
    string Rationale(StrategyFeatures f, StrategyParams p);

    /// <summary>Ranking factors; weights are relative (the scorer renormalises over factors with data).</summary>
    IReadOnlyList<FactorSpec> Factors { get; }

    /// <summary>Weight of the optional sentiment factor (0 to ignore sentiment).</summary>
    decimal SentimentWeight { get; }

    /// <summary>
    /// Near-setups worth watching when nothing qualifies: scored names that aren't triggering yet,
    /// closest first. Empty for strategies without a meaningful "almost" state.
    /// </summary>
    IEnumerable<(StrategyScore Score, string Note)> Watch(IReadOnlyList<StrategyScore> ranked, StrategyParams p);
}
