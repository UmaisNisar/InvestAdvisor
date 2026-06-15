namespace InvestAdvisor.Core.Enums;

/// <summary>The kinds of manual run the bell + run manager can drive from the UI.</summary>
public enum RunKind
{
    /// <summary>Dashboard "Run now": re-analyze holdings, then regenerate today's picks.</summary>
    Dashboard = 0,

    /// <summary>Swing "Re-scan now": rescan the swing universe for setups.</summary>
    Swing = 1,
}
