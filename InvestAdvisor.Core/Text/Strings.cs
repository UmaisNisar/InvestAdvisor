namespace InvestAdvisor.Core.Text;

public static class Strings
{
    /// <summary>Hard-cuts <paramref name="s"/> to at most <paramref name="max"/> chars. Null/empty passes through.</summary>
    public static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s[..max];

    /// <summary>
    /// Cuts to <paramref name="max"/> chars and appends an ellipsis when anything was dropped,
    /// trimming trailing whitespace so the ellipsis never follows a space.
    /// </summary>
    public static string Ellipsize(string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s[..max].TrimEnd() + "…";
}
