namespace InvestAdvisor.Ui.Shared;

/// <summary>
/// Display formatting shared by every page and component, so "3m ago", "$1,234.56" and the
/// gain/loss colour are spelled the same way everywhere. Colours are CSS custom properties
/// (<c>--ia-up</c> / <c>--ia-down</c>, defined in app.css from the theme palette) so they follow
/// light/dark mode; <see cref="UpHex"/>/<see cref="DownHex"/> exist only for chart series, which
/// can't resolve CSS variables.
/// </summary>
public static class Fmt
{
    public const string UpHex = "#34C759";
    public const string DownHex = "#FF383C";

    /// <summary>"just now" · "3m ago" · "2h ago" · "5d ago".</summary>
    public static string Ago(DateTime utc, DateTime? nowUtc = null)
    {
        var d = (nowUtc ?? DateTime.UtcNow) - utc;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }

    /// <summary>Like <see cref="Ago"/> but falls back to a "MMM d" date after a week — for feeds.</summary>
    public static string AgoOrDate(DateTime utc, DateTime? nowUtc = null)
    {
        var d = (nowUtc ?? DateTime.UtcNow) - utc;
        return d.TotalDays < 7 ? Ago(utc, nowUtc) : utc.ToLocalTime().ToString("MMM d");
    }

    /// <summary>"due now" · "in 12m" · "in 3h" · "in 2d".</summary>
    public static string Until(DateTime utc, DateTime? nowUtc = null)
    {
        var d = utc - (nowUtc ?? DateTime.UtcNow);
        if (d <= TimeSpan.Zero) return "due now";
        if (d.TotalMinutes < 60) return $"in {(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"in {(int)d.TotalHours}h";
        return $"in {(int)d.TotalDays}d";
    }

    /// <summary>"$1,234.56" — USD figures on dashboards.</summary>
    public static string Money(decimal v) => "$" + v.ToString("N2");

    /// <summary>"123.45" — a bare price level (entry / stop / target on setup cards).</summary>
    public static string Price(decimal v) => v.ToString("0.00");

    /// <summary>Currency symbol prefix for a native-currency price.</summary>
    public static string CurPrefix(string? currency) =>
        currency switch { "CAD" => "C$", "AUD" => "A$", _ => "$" };

    /// <summary>Cuts to <paramref name="max"/> chars and appends an ellipsis when anything was dropped.</summary>
    public static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s[..max].TrimEnd() + "…";

    /// <summary>Inline style colouring a gain/loss; empty for null (unpriced) so it stays neutral.</summary>
    public static string UpDownStyle(decimal? v) =>
        v is null ? string.Empty : v >= 0 ? "color:var(--ia-up)" : "color:var(--ia-down)";

    /// <summary>CSS class for the up/down/flat pill backgrounds.</summary>
    public static string UpDownClass(decimal? v) => v switch
    {
        null => "ia-chg-flat",
        >= 0 => "ia-chg-up",
        _ => "ia-chg-down",
    };
}
