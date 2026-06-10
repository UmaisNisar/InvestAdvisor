namespace InvestAdvisor.Core.Options;

public sealed class RedditOptions
{
    public const string SectionName = "Reddit";

    /// <summary>Reddit OAuth app client id (from https://www.reddit.com/prefs/apps — "script"/"web app").</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Reddit OAuth app secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Reddit requires a unique, descriptive User-Agent or it throttles/blocks the client.</summary>
    public string UserAgent { get; set; } = "InvestAdvisor/1.0 (sentiment)";

    /// <summary>Subreddits to search, joined with '+' (a Reddit multi-search).</summary>
    public string Subreddits { get; set; } = "wallstreetbets+stocks+investing";

    /// <summary>Drop posts below this score (upvotes) to cut noise. 0 = keep all.</summary>
    public int MinScore { get; set; } = 5;

    public string TokenUrl { get; set; } = "https://www.reddit.com/api/v1/access_token";
    public string BaseUrl { get; set; } = "https://oauth.reddit.com";
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>True only when both client id and secret are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
