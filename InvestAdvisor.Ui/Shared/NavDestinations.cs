using MudBlazor;

namespace InvestAdvisor.Ui.Shared;

/// <summary>One app destination. <paramref name="TabLabel"/> is set for the phone bottom bar's short form.</summary>
public sealed record NavDestination(string Label, string Href, string Icon, string? TabLabel = null)
{
    public bool InBottomBar => TabLabel is not null;
}

/// <summary>
/// The single list of places the app can navigate to. The side rail, the phone bottom bar and
/// the Ctrl+K palette all render from it, so a new page shows up everywhere by adding one row.
/// </summary>
public static class NavDestinations
{
    public static readonly IReadOnlyList<NavDestination> All =
    [
        new("Dashboard", "/", Icons.Material.Filled.Dashboard, TabLabel: "Home"),
        new("Where to invest", "/invest", Icons.Material.Filled.Lightbulb, TabLabel: "Invest"),
        new("Swing setups", "/swing", Icons.Material.Filled.Bolt, TabLabel: "Swing"),
        new("Momentum breakouts", "/momentum", Icons.Material.Filled.RocketLaunch),
        new("Long-term screener", "/screener", Icons.Material.Filled.TrendingUp),
        new("Advice feed", "/advice", Icons.Material.Filled.History),
        new("AI cost", "/costs", Icons.Material.Filled.Paid),
        new("Watchlist", "/watchlist", Icons.Material.Filled.Visibility, TabLabel: "Watchlist"),
        new("Settings", "/settings", Icons.Material.Filled.Settings),
    ];
}
