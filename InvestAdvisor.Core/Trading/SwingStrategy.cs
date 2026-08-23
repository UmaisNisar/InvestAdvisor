using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Trading;

/// <summary>
/// Parameters for the swing strategy. The extra knobs are the regime filter and the two
/// mean-reversion triggers; the shared exit/sizing knobs come from <see cref="StrategyParams"/>.
/// </summary>
public sealed record SwingParams : StrategyParams
{
    /// <summary>Long-term trend filter: only go long when close is above this SMA.</summary>
    public int RegimeSmaPeriod { get; init; } = 200;

    /// <summary>Short SMA used to gauge how far price has pulled back short-term.</summary>
    public int PullbackSmaPeriod { get; init; } = 5;

    /// <summary>Deep-oversold trigger: enter when RSI(<see cref="StrategyParams.RsiPeriod"/>) is at or below this.</summary>
    public decimal OversoldEntry { get; init; } = 25m;

    /// <summary>Whether the gentler "pullback to the 50-day MA" setup is also active.</summary>
    public bool EnableMaBounce { get; init; } = true;

    /// <summary>MA-bounce: price must be within this fraction above the 50-day SMA (pulled back to it).</summary>
    public decimal MaBounceBandPct { get; init; } = 0.04m;

    /// <summary>MA-bounce: RSI must be below this (mildly soft, not necessarily deeply oversold).</summary>
    public decimal MaBounceRsiMax { get; init; } = 50m;

    public SwingParams()
    {
        // Connors-style short RSI suits a 2–3 day hold; wide stop because mean reversion catches a dip;
        // modest target so the bounce is reachable inside the hold.
        RsiPeriod = 3;
        StopAtrMultiple = 2.5m;
        TargetAtrMultiple = 1.5m;
        RiskPerTradePct = 1.0m;
        MaxPositionPct = 25m;
        SetupCount = 5;
        MinAvgDollarVolume = 5_000_000m;
        RoundTripCost = 0.001m;
        MinProfitFactor = 1.15m;
    }

    public static readonly SwingParams Default = For(RiskLevel.Medium);

    /// <summary>
    /// Preset for a risk level. The dial moves four things together: how oversold a name must be to
    /// trigger (looser = more trades), whether the gentler MA-bounce setup is on, how much capital
    /// each trade risks, and how many names are surfaced.
    /// </summary>
    public static SwingParams For(RiskLevel level) => level switch
    {
        RiskLevel.Low => new SwingParams
        {
            OversoldEntry = 15m, EnableMaBounce = false, StopAtrMultiple = 3.0m,
            RiskPerTradePct = 0.5m, MaxPositionPct = 15m, SetupCount = 3,
        },
        RiskLevel.High => new SwingParams
        {
            OversoldEntry = 35m, EnableMaBounce = true, StopAtrMultiple = 2.0m,
            RiskPerTradePct = 1.5m, MaxPositionPct = 35m, SetupCount = 8,
        },
        _ => new SwingParams
        {
            OversoldEntry = 25m, EnableMaBounce = true, StopAtrMultiple = 2.5m,
            RiskPerTradePct = 1.0m, MaxPositionPct = 25m, SetupCount = 5,
        },
    };
}

/// <summary>The raw indicator readings behind a swing score.</summary>
public sealed record SwingFeatures(
    decimal Close,
    decimal Atr,
    decimal? RegimeSma,
    decimal? TrendSma,
    decimal? Rsi,
    decimal? PullbackPct,
    decimal? RelativeVolume,
    decimal? AverageDollarVolume)
    : StrategyFeatures(Close, Atr, TrendSma, Rsi, RelativeVolume, AverageDollarVolume)
{
    /// <summary>In a confirmed long-term up-trend (the only regime we go long in).</summary>
    public bool AboveRegime => RegimeSma is { } s && s > 0m && Close > s;

    /// <summary>How far above the 200-day SMA, as a fraction — uptrend health (and over-extension).</summary>
    public decimal? RegimeDistancePct => RegimeSma is { } s && s > 0m ? (Close - s) / s : null;
}

/// <summary>
/// <b>Regime-filtered short-term mean reversion</b>: only buy names in a confirmed long-term
/// up-trend (above the 200-day SMA), and only when they've pulled back to a short-term oversold
/// extreme (low RSI(3)) — then ride the bounce for 2–3 days with a wide ATR stop. The opposite of
/// momentum-chasing, which doesn't pay on a 2–3 day hold.
/// </summary>
public sealed class SwingStrategy : IStrategy
{
    public static readonly SwingStrategy Instance = new();

    public StrategyKind Kind => StrategyKind.Swing;

    public StrategyParams ParamsFor(RiskLevel level) => SwingParams.For(level);

    public int Warmup(StrategyParams p) => Math.Max(P(p).RegimeSmaPeriod, p.AtrPeriod) + 1;

    public StrategyFeatures? Read(IReadOnlyList<Candle> candles, StrategyParams parameters)
    {
        var p = P(parameters);
        if (candles.Count < p.RegimeSmaPeriod + 1) return null; // need a full 200-day regime window
        if (Indicators.Atr(candles, p.AtrPeriod) is not { } atr || atr <= 0m) return null;

        var closes = Indicators.Closes(candles);
        var latest = candles[^1];
        var pullbackSma = Indicators.Sma(closes, p.PullbackSmaPeriod);

        return new SwingFeatures(
            Close: latest.Close,
            Atr: atr,
            RegimeSma: Indicators.Sma(closes, p.RegimeSmaPeriod),
            TrendSma: Indicators.Sma(closes, p.TrendSmaPeriod),
            Rsi: Indicators.Rsi(closes, p.RsiPeriod),
            PullbackPct: pullbackSma is { } ps && ps > 0m ? (ps - latest.Close) / ps : null,
            RelativeVolume: Indicators.RelativeVolume(candles, p.VolumeLookback),
            AverageDollarVolume: Indicators.AverageDollarVolume(candles, p.VolumeLookback));
    }

    /// <summary>
    /// A long entry qualifies — within a confirmed long-term up-trend — on either a deep oversold dip
    /// or, when enabled, a gentler pullback to the rising 50-day MA. Both are "buy the dip".
    /// </summary>
    public bool Qualifies(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        return f.AboveRegime && f.Atr > 0m && (IsDeepOversold(f, p) || IsMaBounce(f, p));
    }

    public SetupKind KindOf(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        return IsDeepOversold(f, p) ? SetupKind.Primary : IsMaBounce(f, p) ? SetupKind.Secondary : SetupKind.None;
    }

    public string Rationale(StrategyFeatures features, StrategyParams parameters)
    {
        var (f, p) = (F(features), P(parameters));
        var parts = new List<string>();
        if (f.RegimeDistancePct is { } d) parts.Add($"{d * 100m:0.0}% above 200-day MA (up-trend)");
        if (IsDeepOversold(f, p) && f.Rsi is { } r) parts.Add($"RSI({p.RsiPeriod}) {r:0} — oversold pullback");
        else if (IsMaBounce(f, p)) parts.Add($"pulled back to the {p.TrendSmaPeriod}-day MA");
        else if (f.Rsi is { } r2) parts.Add($"RSI({p.RsiPeriod}) {r2:0}");
        if (f.RelativeVolume is { } rv and > 1.3m) parts.Add($"{rv:0.0}× volume");
        return (parts.Count == 0 ? "No clear edge" : string.Join(", ", parts))
            + $". Buy the bounce: target +{p.TargetAtrMultiple:0.0}×ATR, stop −{p.StopAtrMultiple:0.0}×ATR, hold ≤{p.HoldingDays} sessions.";
    }

    // Sub-score weights (sum 100 with sentiment) for the mean-reversion thesis: the oversold trigger
    // and the up-trend regime lead; pullback depth and a volume/sentiment confirm trim.
    public IReadOnlyList<FactorSpec> Factors { get; } =
    [
        // Oversold: a lower RSI is a better mean-reversion entry, so rank it lower-better.
        new("Oversold", f => f.Rsi, HigherBetter: false, Weight: 30m),
        // Regime: more above the 200-day SMA = a healthier up-trend to fade the dip into.
        new("Regime", f => F(f).RegimeDistancePct, HigherBetter: true, Weight: 25m),
        // Pullback depth: a deeper short-term dip has more to revert.
        new("Pullback", f => F(f).PullbackPct, HigherBetter: true, Weight: 20m),
        new("Volume", f => f.RelativeVolume, HigherBetter: true, Weight: 10m),
    ];

    public decimal SentimentWeight => 15m;

    /// <summary>Names in a confirmed up-trend that haven't pulled back far enough to trigger yet, closest first (lowest RSI).</summary>
    public IEnumerable<(StrategyScore Score, string Note)> Watch(IReadOnlyList<StrategyScore> ranked, StrategyParams p) =>
        ranked
            .Where(s => !s.Qualifies && F(s.Features).AboveRegime && s.Features.Rsi is not null)
            .OrderBy(s => s.Features.Rsi)
            .Select(s => (s, $"In an up-trend; RSI({p.RsiPeriod}) {s.Features.Rsi:0}. Triggers if it dips a bit more."));

    public static bool IsDeepOversold(SwingFeatures f, SwingParams p) => f.Rsi is { } r && r <= p.OversoldEntry;

    public static bool IsMaBounce(SwingFeatures f, SwingParams p) =>
        p.EnableMaBounce
        && f.TrendDistancePct is { } td && td >= 0m && td <= p.MaBounceBandPct
        && f.Rsi is { } r && r <= p.MaBounceRsiMax;

    private static SwingParams P(StrategyParams p) => (SwingParams)p;
    private static SwingFeatures F(StrategyFeatures f) => (SwingFeatures)f;
}
