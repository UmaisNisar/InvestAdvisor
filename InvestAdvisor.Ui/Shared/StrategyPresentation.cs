using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Trading;

namespace InvestAdvisor.Ui.Shared;

/// <summary>Everything about a strategy that is copy rather than math: titles, tooltips, labels, routes.</summary>
public sealed record StrategyPresentation(
    StrategyKind Kind,
    string Route,
    RunKind RunKind,
    string Title,
    string TitleTooltip,
    string RiskTooltip,
    string RescanTooltip,
    string UniverseNoun,
    string SetupsHeading,
    string ValidatedNote,
    string PaperOnlyNote,
    string NoSetupsNote,
    string BacktestCaveat,
    string Disclaimer,
    string ScoreTooltip,
    string SizeTooltip,
    string PrimaryLabel,
    string PrimaryTooltip,
    string SecondaryLabel,
    string SecondaryTooltip,
    StrategyPresentation.CrossLinkInfo CrossLink)
{
    public sealed record CrossLinkInfo(string Href, string Label, string Icon);

    public string KindLabel(SetupKind k) => k switch
    {
        SetupKind.Primary => PrimaryLabel,
        SetupKind.Secondary => SecondaryLabel,
        _ => "",
    };

    public string KindTooltip(SetupKind k) => k switch
    {
        SetupKind.Primary => PrimaryTooltip,
        SetupKind.Secondary => SecondaryTooltip,
        _ => "",
    };

    public static StrategyPresentation For(StrategyKind kind) => kind == StrategyKind.Swing ? Swing : Momentum;

    public static StrategyPresentation ForRoute(string path) =>
        path.TrimStart('/').StartsWith("momentum", StringComparison.OrdinalIgnoreCase) ? Momentum : Swing;

    public static readonly StrategyPresentation Swing = new(
        Kind: StrategyKind.Swing,
        Route: "/swing",
        RunKind: RunKind.Swing,
        Title: "Swing setups",
        TitleTooltip: "Short-horizon (2–3 day) setups: buy liquid US + TSX names that are in an up-trend (above their 200-day average) but short-term oversold — a pullback to buy, then ride the bounce. Each idea is risk-bounded: entry, stop, target and size. Research, not advice.",
        RiskTooltip: "Risk dial. Higher = looser oversold trigger, more setup types, bigger position sizing and more names surfaced (more trades, lower conviction each). Lower = stricter and smaller. Changing it re-scans.",
        RescanTooltip: "Re-scan the universe now and refresh today's setups at current levels. Resolves any open paper trades too. Takes up to a minute (fetches bars for every name).",
        UniverseNoun: "liquid names",
        SetupsHeading: "Today's setups",
        ValidatedNote: "Still probabilistic — size with the stop, never bet the farm.",
        PaperOnlyNote: "These setups have not cleared the backtest + paper-trade gate (a real edge — profit factor ≥1.15 over a meaningful sample, not break-even noise). Treat them as practice: paper-trade them and watch the track record before risking real money. Short-horizon trading loses money for most people.",
        NoSetupsNote: "nothing is on a buyable pullback (up-trend + oversold). The scanner waits rather than chasing strength. Try a higher risk level, or watch the names below that are closest to triggering.",
        BacktestCaveat: "Backtests overstate live results (no liquidity/slippage beyond a flat cost, survivorship in the universe). A sanity check, not a promise.",
        Disclaimer: "Always honour the stop. Higher risk = more setups but lower conviction each — it is a trade-off, not free.",
        ScoreTooltip: "Relative swing score across the universe (technical factors). Higher = stronger setup, not a profit guarantee.",
        SizeTooltip: "Suggested size so a stop-out loses ~1% of capital. Caps at 25%.",
        PrimaryLabel: "Oversold · A",
        PrimaryTooltip: "Deep short-term oversold dip — the higher-conviction mean-reversion setup.",
        SecondaryLabel: "MA bounce · B",
        SecondaryTooltip: "Gentler pullback to the rising 50-day average — a lower-conviction setup, only surfaced at Medium/High risk.",
        CrossLink: new("/momentum", "Momentum breakouts →", MudBlazor.Icons.Material.Filled.RocketLaunch));

    public static readonly StrategyPresentation Momentum = new(
        Kind: StrategyKind.Momentum,
        Route: "/momentum",
        RunKind: RunKind.Momentum,
        Title: "Momentum breakouts",
        TitleTooltip: "High-volatility breakout setups: buy high-beta US + Canadian names that break a tight consolidation on a volume surge, riding the expansion for a few sessions toward a ~10% target. Tight stop, asymmetric reward. Far riskier than swing — most breakouts fail. Research, not advice.",
        RiskTooltip: "Risk dial. Higher = looser base/volume quality, tighter stop, bigger position sizing and more names (more trades, lower conviction each). Lower = stricter and smaller. Changing it re-scans.",
        RescanTooltip: "Re-scan the universe now and refresh today's setups at current levels. Resolves any open paper trades too. Takes up to a minute (fetches bars for every name).",
        UniverseNoun: "high-volatility names",
        SetupsHeading: "Today's breakouts",
        ValidatedNote: "Still probabilistic — size with the stop, never bet the farm.",
        PaperOnlyNote: "These breakout setups have not cleared the backtest gate (a real edge — profit factor ≥1.30 over a meaningful sample, stricter than swing because breakouts slip more). Treat them as practice and watch the track record before risking real money. High-volatility momentum loses money for most people — expect frequent stop-outs.",
        NoSetupsNote: "nothing is breaking a tight base on volume. The scanner waits rather than forcing a trade. Try a higher risk level or re-scan later.",
        BacktestCaveat: "Backtests overstate live results (breakout slippage beyond a flat cost, survivorship in the universe). A sanity check, not a promise.",
        Disclaimer: "Always honour the stop. This is the aggressive sleeve: higher reward, far higher chance of loss on each trade.",
        ScoreTooltip: "Relative momentum score across the universe (breakout/squeeze/volume/volatility factors). Higher = stronger setup, not a profit guarantee.",
        SizeTooltip: "Suggested size from the per-trade risk budget and stop distance. Caps at the risk level's max.",
        PrimaryLabel: "Squeeze · A",
        PrimaryTooltip: "Break of a tight consolidation on a volume surge — the higher-conviction breakout setup.",
        SecondaryLabel: "Continuation · B",
        SecondaryTooltip: "Strong trailing momentum carrying through a breakout, no tight base required — a lower-conviction setup, only surfaced at Medium/High risk.",
        CrossLink: new("/swing", "Swing setups →", MudBlazor.Icons.Material.Filled.Bolt));
}
