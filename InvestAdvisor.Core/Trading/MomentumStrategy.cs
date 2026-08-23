using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Trading;

/// <summary>
/// Parameters for the momentum strategy. The extra knobs are the breakout/base/volatility gates
/// and the two breakout triggers; the shared exit/sizing knobs come from <see cref="StrategyParams"/>.
/// </summary>
public sealed record MomentumParams : StrategyParams
{
    /// <summary>Lookback whose prior high the close must break to count as a breakout.</summary>
    public int BreakoutLookback { get; init; } = 20;

    /// <summary>Window used to measure the pre-breakout consolidation base's tightness.</summary>
    public int BasePeriod { get; init; } = 10;

    public int MomentumLookback { get; init; } = 20;

    /// <summary>Volatility floor: ATR as a fraction of price must be at least this — keeps a 10% target reachable.</summary>
    public decimal MinAtrPercent { get; init; } = 0.035m;

    /// <summary>
    /// Base must be no wider than this many ATRs to count as a tight "squeeze". Measured in ATR units
    /// (not a fixed % of price) so the bar scales with the name's own volatility.
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

    public MomentumParams()
    {
        // Tight stop because a real breakout shouldn't come back; wide asymmetric target — the move
        // we're paying for. Lower liquidity floor than swing (smaller-cap pool) but enough to fill a
        // small position cleanly. Higher friction + stricter gate: breakouts slip more.
        RsiPeriod = 14;
        StopAtrMultiple = 1.25m;
        TargetAtrMultiple = 2.5m;
        RiskPerTradePct = 1.5m;
        MaxPositionPct = 30m;
        SetupCount = 5;
        MinAvgDollarVolume = 1_000_000m;
        RoundTripCost = 0.0015m;
        MinProfitFactor = 1.3m;
    }

    public static readonly MomentumParams Default = For(RiskLevel.Medium);

    /// <summary>
    /// Preset for a risk level. The dial moves quality (base tightness + volume confirmation), the
    /// stop multiple, per-trade capital at risk, and how many names surface. High targets a ~10%
    /// move (≈2.5×ATR on a ≥4%-ATR name).
    /// </summary>
    public static MomentumParams For(RiskLevel level) => level switch
    {
        RiskLevel.Low => new MomentumParams
        {
            MinAtrPercent = 0.035m, MaxBaseRangeAtr = 2.5m, MinRelativeVolume = 2.0m,
            EnableContinuation = false, StopAtrMultiple = 1.5m, TargetAtrMultiple = 3.0m,
            RiskPerTradePct = 0.75m, MaxPositionPct = 20m, SetupCount = 3,
        },
        RiskLevel.High => new MomentumParams
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

/// <summary>The raw indicator readings behind a momentum score.</summary>
public sealed record MomentumFeatures(
    decimal Close,
    decimal Atr,
    decimal? TrendSma,
    decimal? AtrPercent,
    decimal? BreakoutStrength,
    decimal? BaseRangePct,
    decimal? RelativeVolume,
    decimal? Rsi,
    decimal? MomentumReturn,
    decimal? AverageDollarVolume)
    : StrategyFeatures(Close, Atr, TrendSma, Rsi, RelativeVolume, AverageDollarVolume)
{
    /// <summary>Volatile enough that a double-digit move in the hold window is plausible.</summary>
    public bool IsHighVolatility(MomentumParams p) => AtrPercent is { } a && a >= p.MinAtrPercent;

    /// <summary>
    /// Consolidation base width in ATRs (range ÷ ATR) — tightness that scales with the name's own
    /// volatility, so a high-beta mover isn't unfairly judged "loose" just for being volatile.
    /// </summary>
    public decimal? BaseRangeAtr => BaseRangePct is { } r && Atr > 0m ? r * Close / Atr : null;
}

/// <summary>
/// <b>Volatility-expansion breakout</b>: among names volatile enough that a ~10% move in 3 sessions
/// is plausible, buy the break of a tight consolidation on a volume surge, riding the expansion for
/// a few days with a tight ATR stop and an asymmetric target. Momentum — the opposite of the swing
/// strategy's mean reversion.
/// </summary>
public sealed class MomentumStrategy : IStrategy
{
    public static readonly MomentumStrategy Instance = new();

    public StrategyKind Kind => StrategyKind.Momentum;

    public StrategyParams ParamsFor(RiskLevel level) => MomentumParams.For(level);

    public int Warmup(StrategyParams parameters)
    {
        var p = P(parameters);
        return Math.Max(Math.Max(p.TrendSmaPeriod, p.BreakoutLookback), p.AtrPeriod) + 1;
    }

    public StrategyFeatures? Read(IReadOnlyList<Candle> candles, StrategyParams parameters)
    {
        var p = P(parameters);
        if (Indicators.Atr(candles, p.AtrPeriod) is not { } atr || atr <= 0m) return null;

        var closes = Indicators.Closes(candles);
        var latest = candles[^1];

        return new MomentumFeatures(
            Close: latest.Close,
            Atr: atr,
            TrendSma: Indicators.Sma(closes, p.TrendSmaPeriod),
            AtrPercent: Indicators.AtrPercent(candles, p.AtrPeriod),
            BreakoutStrength: Indicators.BreakoutStrength(candles, p.BreakoutLookback),
            BaseRangePct: Indicators.ConsolidationRangePct(candles, p.BasePeriod),
            RelativeVolume: Indicators.RelativeVolume(candles, p.VolumeLookback),
            Rsi: Indicators.Rsi(closes, p.RsiPeriod),
            MomentumReturn: Indicators.Return(candles, p.MomentumLookback),
            AverageDollarVolume: Indicators.AverageDollarVolume(candles, p.VolumeLookback));
    }

    /// <summary>
    /// A long entry qualifies — in a high-volatility name above its intermediate trend — on either a
    /// squeeze breakout (tight base + volume surge) or, when enabled, a strong momentum continuation.
    /// </summary>
    public bool Qualifies(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        return f.AboveTrend && f.Atr > 0m && f.IsHighVolatility(p) && (IsSqueezeBreakout(f, p) || IsContinuation(f, p));
    }

    public SetupKind KindOf(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        return IsSqueezeBreakout(f, p) ? SetupKind.Primary : IsContinuation(f, p) ? SetupKind.Secondary : SetupKind.None;
    }

    public string Rationale(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        var parts = new List<string>();
        if (f.BreakoutStrength is { } b and > 0m) parts.Add($"broke {p.BreakoutLookback}-day high (+{b * 100m:0.0}%)");
        if (IsSqueezeBreakout(f, p) && f.BaseRangeAtr is { } range)
            parts.Add($"out of a {range:0.0}×ATR base (squeeze)");
        else if (IsContinuation(f, p) && f.MomentumReturn is { } m)
            parts.Add($"+{m * 100m:0.0}% {p.MomentumLookback}-day momentum");
        if (f.RelativeVolume is { } rv and > 1.3m) parts.Add($"{rv:0.0}× volume");
        if (f.AtrPercent is { } a) parts.Add($"{a * 100m:0.0}% daily range");
        return (parts.Count == 0 ? "No clear edge" : string.Join(", ", parts))
            + $". Ride the expansion: target +{p.TargetAtrMultiple:0.0}×ATR, stop −{p.StopAtrMultiple:0.0}×ATR, hold ≤{p.HoldingDays} sessions.";
    }

    // Sub-score weights (sum 100 with sentiment): the breakout and the squeeze (tight base) lead the
    // thesis; volume and trailing momentum confirm; volatility keeps the 10%-reachable bias.
    public IReadOnlyList<FactorSpec> Factors { get; } =
    [
        new("Breakout", f => F(f).BreakoutStrength, HigherBetter: true, Weight: 25m),
        // Squeeze: a tighter base (fewer ATRs of range) is a better coil, so rank it lower-better.
        new("Squeeze", f => F(f).BaseRangeAtr, HigherBetter: false, Weight: 20m),
        new("Volume", f => f.RelativeVolume, HigherBetter: true, Weight: 15m),
        new("Volatility", f => F(f).AtrPercent, HigherBetter: true, Weight: 15m),
        new("Momentum", f => F(f).MomentumReturn, HigherBetter: true, Weight: 15m),
    ];

    public decimal SentimentWeight => 10m;

    /// <summary>Breakouts have no meaningful "almost" state — a base either broke or it didn't.</summary>
    public IEnumerable<(StrategyScore Score, string Note)> Watch(IReadOnlyList<StrategyScore> ranked, StrategyParams p) => [];

    public static bool IsSqueezeBreakout(MomentumFeatures f, MomentumParams p) =>
        f.BreakoutStrength is { } b && b > p.BreakoutMargin
        && f.BaseRangeAtr is { } range && range <= p.MaxBaseRangeAtr
        && f.RelativeVolume is { } rv && rv >= p.MinRelativeVolume;

    public static bool IsContinuation(MomentumFeatures f, MomentumParams p) =>
        p.EnableContinuation
        && f.BreakoutStrength is { } b && b > p.BreakoutMargin
        && f.MomentumReturn is { } m && m >= p.MinContinuationReturn
        && f.RelativeVolume is { } rv && rv >= p.MinRelativeVolume;

    private static MomentumParams P(StrategyParams p) => (MomentumParams)p;
    private static MomentumFeatures F(StrategyFeatures f) => (MomentumFeatures)f;
}
