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
/// limits. Read-only. Degrades to empty on any failure.
/// </summary>
public sealed class BlueskyProvider(
    HttpClient http,
    IOptions<BlueskyOptions> options,
    ISystemClock clock,
    ILogger<BlueskyProvider>? logger = null) : ISocialFeedProvider
{
    private const int Limit = 25;
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(100); // bsky JWTs last ~2h
    private readonly BlueskyOptions _opts = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;

    public NewsSource Channel => NewsSource.Bluesky;

    public async Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default)
    {
        if (!_opts.Enabled || string.IsNullOrWhiteSpace(ticker)) return Array.Empty<SocialPost>();

        var symbol = ticker.Trim().ToUpperInvariant();
        var token = _opts.HasCredentials ? await GetTokenAsync(ct) : null;

        var url = $"{_opts.AppViewUrl.TrimEnd('/')}/xrpc/app.bsky.feed.searchPosts?" +
                  $"q={Uri.EscapeDataString("$" + symbol)}&limit={Limit}&sort=latest";

        SearchResponse? payload;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger?.LogWarning("Bluesky search for {Ticker} returned {Status}.", symbol, resp.StatusCode);
                return Array.Empty<SocialPost>();
            }
            payload = await resp.Content.ReadFromJsonAsync<SearchResponse>(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Bluesky search failed for {Ticker}.", symbol);
            return Array.Empty<SocialPost>();
        }

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

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && clock.UtcNow < _tokenExpiresUtc) return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.UtcNow < _tokenExpiresUtc) return _token;

            var url = $"{_opts.AuthUrl.TrimEnd('/')}/xrpc/com.atproto.server.createSession";
            using var resp = await http.PostAsJsonAsync(url,
                new { identifier = _opts.Identifier, password = _opts.AppPassword }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger?.LogWarning("Bluesky createSession failed: {Status}.", resp.StatusCode);
                return null;
            }
            var session = await resp.Content.ReadFromJsonAsync<SessionResponse>(ct);
            if (session is null || string.IsNullOrWhiteSpace(session.AccessJwt)) return null;

            _token = session.AccessJwt;
            _tokenExpiresUtc = clock.UtcNow.Add(TokenTtl);
            return _token;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Bluesky createSession failed.");
            return null;
        }
        finally { _tokenLock.Release(); }
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
