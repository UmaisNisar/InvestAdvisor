using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Core.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Reddit;

/// <summary>
/// Searches investing subreddits for ticker mentions via Reddit's read-only OAuth (client-credentials
/// grant). Fetches a bearer token on first use and caches it until expiry. Read-only: no posting.
/// </summary>
public sealed class RedditProvider : SocialFeedProviderBase
{
    private const int Limit = 25;
    private readonly HttpClient _http;
    private readonly RedditOptions _opts;
    private readonly ILogger<RedditProvider>? _logger;
    private readonly CachedBearerToken _token;

    public RedditProvider(
        HttpClient http,
        IOptions<RedditOptions> options,
        ISystemClock clock,
        ILogger<RedditProvider>? logger = null) : base(logger, "Reddit")
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
        _token = new CachedBearerToken(clock, AcquireTokenAsync, logger, "Reddit");
    }

    public override NewsSource Channel => NewsSource.Reddit;
    protected override bool Enabled => _opts.IsConfigured;

    protected override async Task<IReadOnlyList<SocialPost>> FetchAsync(string symbol, CancellationToken ct)
    {
        var token = await _token.GetAsync(ct);
        if (token is null) return Array.Empty<SocialPost>();

        var url = $"{_opts.BaseUrl.TrimEnd('/')}/r/{_opts.Subreddits}/search?" +
                  $"q={Uri.EscapeDataString(symbol)}&restrict_sr=true&sort=new&limit={Limit}&t=week";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
        req.Headers.UserAgent.ParseAdd(_opts.UserAgent);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return EmptyForStatus(symbol, resp.StatusCode);
        var listing = await resp.Content.ReadFromJsonAsync<Listing>(ct);

        var children = listing?.Data?.Children;
        if (children is null || children.Length == 0) return Array.Empty<SocialPost>();

        return children
            .Select(c => c.Data)
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d!.Title) && !string.IsNullOrWhiteSpace(d.Permalink))
            .Where(d => d!.Score >= _opts.MinScore)
            .Select(d => new SocialPost(
                Ticker: symbol,
                Text: Compose(d!),
                Source: $"r/{d!.Subreddit}",
                Url: "https://www.reddit.com" + d.Permalink,
                CreatedAtUtc: DateTimeOffset.FromUnixTimeSeconds((long)d.CreatedUtc).UtcDateTime,
                Channel: NewsSource.Reddit))
            .ToArray();
    }

    private static string Compose(Post p) =>
        Strings.Truncate(string.IsNullOrWhiteSpace(p.SelfText) ? p.Title! : $"{p.Title}. {p.SelfText}", 1000);

    private async Task<(string Token, TimeSpan Ttl)?> AcquireTokenAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
            }),
        };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Headers.UserAgent.ParseAdd(_opts.UserAgent);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Reddit token request failed: {Status}.", resp.StatusCode);
            return null;
        }

        var tok = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (tok is null || string.IsNullOrWhiteSpace(tok.AccessToken)) return null;
        // Refresh a minute early to avoid edge-of-expiry 401s.
        return (tok.AccessToken, TimeSpan.FromSeconds(Math.Max(0, tok.ExpiresIn - 60)));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record Listing([property: JsonPropertyName("data")] ListingData? Data);

    private sealed record ListingData([property: JsonPropertyName("children")] Child[]? Children);

    private sealed record Child([property: JsonPropertyName("data")] Post? Data);

    private sealed record Post(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("selftext")] string? SelfText,
        [property: JsonPropertyName("permalink")] string? Permalink,
        [property: JsonPropertyName("subreddit")] string? Subreddit,
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("created_utc")] double CreatedUtc);
}
