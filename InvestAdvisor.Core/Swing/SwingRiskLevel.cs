namespace InvestAdvisor.Core.Swing;

/// <summary>
/// How aggressively the swing scanner surfaces setups — the user-facing risk dial. Higher risk
/// loosens the oversold trigger, enables more setup types, sizes positions larger, and surfaces more
/// names; lower risk is stricter and smaller. It is a genuine quality/quantity (and exposure)
/// trade-off, not a free lunch: <see cref="High"/> trades more often but each trade is lower-conviction.
/// </summary>
public enum SwingRiskLevel
{
    /// <summary>Strict: only deep oversold dips, small size, few names. Fewer but higher-conviction.</summary>
    Low = 0,

    /// <summary>Balanced default: moderate oversold + pullback-to-50-day-MA setups, standard sizing.</summary>
    Medium = 1,

    /// <summary>Aggressive: shallower pullbacks qualify, larger size, more names. More setups, more noise.</summary>
    High = 2,
}
