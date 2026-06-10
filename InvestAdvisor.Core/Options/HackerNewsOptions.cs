namespace InvestAdvisor.Core.Options;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    /// <summary>Opt-in switch. Off by default. No credentials needed (Algolia HN search is public).</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://hn.algolia.com";

    /// <summary>Drop low-engagement items below this point count (0 = keep all). Cuts noise.</summary>
    public int MinPoints { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}
