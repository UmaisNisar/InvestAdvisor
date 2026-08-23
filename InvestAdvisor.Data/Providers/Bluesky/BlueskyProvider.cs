using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Bluesky;

/// <summary>
/// Searches Bluesky (AT Protocol) for cashtag mentions via the public AppView. Works unauthenticated;
/// if an app password is supplied it first creates a session and sends the bearer token to raise rate
/// limits. Read-only.
/// </summary>
public sealed class BlueskyProvider : SocialFeedProviderBase
{
    private const int Limit = 25;
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(100); // bsky JWTs last ~2h
    private readonly HttpClient _http;
    private readonly BlueskyOptions _opts;
    private readonly ILogger<BlueskyProvider>? _logger;
    private readonly CachedBearerToken _token;

    public BlueskyProvider(
        HttpClient http,
        IOptions<BlueskyOptions> options,
        ISystemClock clock,
        ILogger<BlueskyProvider>? logger = null) : base(logger, "Bluesky")
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
        _token = new CachedBearerToken(clock, CreateSessionAsync, logger, "Bluesky");
    }

    public override NewsSource Channel => NewsSource.Bluesky;
    protected override bool Enabled => _opts.Enabled;

    protected override async Task<IReadOnlyList<SocialPost>> FetchAsync(string symbol, CancellationToken ct)
    {
        var token = _opts.HasCredentials ? await _token.GetAsync(ct) : null;

        var url = $"{_opts.AppViewUrl.TrimEnd('/')}/xrpc/app.bsky.feed.searchPosts?" +
                  $"q={Uri.EscapeDataString("$" + symbol)}&limit={Limit}&sort=latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return EmptyForStatus(symbol, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<SearchResponse>(ct);

        if (payload?.Posts is null || payload.Posts.Length == 0) return Array.Empty<SocialPost>();

        return payload.Posts
            .Where(p => p is { Uri: not null } && !string.IsNullOrWhiteSpace(p.Record?.Text))
            .Select(p => new SocialPost(
                Ticker: symbol,
                Text: p.Record!.Text!,
                Source: "Bluesky",
                Url: Permalink(p),
                CreatedAtUtc: (p.Record.CreatedAt ?? p.IndexedAt).UtcDateTime,
                Channel: NewsSource.Bluesky))
            .ToArray();
    }

    private static string Permalink(Post p)
    {
        // at://did:plc:xxx/app.bsky.feed.post/<rkey>  ->  https://bsky.app/profile/<handle>/post/<rkey>
        var rkey = p.Uri!.Split('/').LastOrDefault();
        var handle = string.IsNullOrWhiteSpace(p.Author?.Handle) ? p.Author?.Did ?? "unknown" : p.Author!.Handle;
        return string.IsNullOrWhiteSpace(rkey)
            ? $"https://bsky.app/profile/{handle}"
            : $"https://bsky.app/profile/{handle}/post/{rkey}";
    }

    private async Task<(string Token, TimeSpan Ttl)?> CreateSessionAsync(CancellationToken ct)
    {
        var url = $"{_opts.AuthUrl.TrimEnd('/')}/xrpc/com.atproto.server.createSession";
        using var resp = await _http.PostAsJsonAsync(url,
            new { identifier = _opts.Identifier, password = _opts.AppPassword }, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Bluesky createSession failed: {Status}.", resp.StatusCode);
            return null;
        }
        var session = await resp.Content.ReadFromJsonAsync<SessionResponse>(ct);
        if (session is null || string.IsNullOrWhiteSpace(session.AccessJwt)) return null;
        return (session.AccessJwt, TokenTtl);
    }

    private sealed record SessionResponse([property: JsonPropertyName("accessJwt")] string? AccessJwt);

    private sealed record SearchResponse([property: JsonPropertyName("posts")] Post[]? Posts);

    private sealed record Post(
        [property: JsonPropertyName("uri")] string? Uri,
        [property: JsonPropertyName("author")] Author? Author,
        [property: JsonPropertyName("record")] Record? Record,
        [property: JsonPropertyName("indexedAt")] DateTimeOffset IndexedAt);

    private sealed record Author(
        [property: JsonPropertyName("handle")] string? Handle,
        [property: JsonPropertyName("did")] string? Did);

    private sealed record Record(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt);
}
