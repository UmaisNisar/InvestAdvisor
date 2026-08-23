namespace InvestAdvisor.Core.Models;

public static class Currency
{
    public const string Usd = "USD";

    /// <summary>Upper-cased, trimmed ISO code; blank → USD (the app's base currency).</summary>
    public static string Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? Usd : code.Trim().ToUpperInvariant();
}
