namespace InvestAdvisor.Core.Options;

public sealed class StockTwitsOptions
{
    public const string SectionName = "StockTwits";

    /// <summary>Whether to pull from StockTwits at all. Off by default (opt-in social source).</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://api.stocktwits.com";

    /// <summary>Optional OAuth token. The public symbol stream works without one, just rate-limited harder.</summary>
    public string AccessToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 15;
}
