namespace InvestAdvisor.Core.Options;

public sealed class BlueskyOptions
{
    public const string SectionName = "Bluesky";

    /// <summary>Opt-in switch. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional handle/email for app-password auth. Public search works without it,
    /// but an authenticated session raises rate limits and search reliability.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>App password (https://bsky.app/settings/app-passwords) — NOT the account password.</summary>
    public string AppPassword { get; set; } = string.Empty;

    /// <summary>Public AppView host that serves read endpoints (search).</summary>
    public string AppViewUrl { get; set; } = "https://public.api.bsky.app";

    /// <summary>Entryway host used to create an auth session when credentials are supplied.</summary>
    public string AuthUrl { get; set; } = "https://bsky.social";

    public int TimeoutSeconds { get; set; } = 15;

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Identifier) && !string.IsNullOrWhiteSpace(AppPassword);
}
