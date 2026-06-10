using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Reddit;

/// <summary>
/// Searches investing subreddits for ticker mentions via Reddit's read-only OAuth (client-credentials
/// grant). Fetches a bearer token on first use and caches it until expiry. Degrades to empty on any
/// failure (including missing credentials), so it never stalls a refresh. Read-only: no posting.
/// </summary>
public sealed class RedditProvider(
    HttpClient http,
    IOptions<RedditOptions> options,
    ISystemClock clock,
    ILogger<RedditProvider>? logger = null) : ISocialFeedProvider
{
    private const int Limit = 25;
    private readonly RedditOptions _opts = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;

    public NewsSource Channel => NewsSource.Reddit;

    public async Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default)
    {
        if (!_opts.IsConfigured || string.IsNullOrWhiteSpace(ticker)) return Array.Empty<SocialPost>();

        var token = await GetTokenAsync(ct);
        if (token is null) return Array.Empty<SocialPost>();

        var symbol = ticker.Trim().ToUpperInvariant();
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/r/{_opts.Subreddits}/search?" +
                  $"q={Uri.EscapeDataString(symbol)}&restrict_sr=true&sort=new&limit={Limit}&t=week";

        Listing? listing;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
            req.Headers.UserAgent.ParseAdd(_opts.UserAgent);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger?.LogWarning("Reddit search for {Ticker} returned {Status}.", symbol, resp.StatusCode);
                return Array.Empty<SocialPost>();
            }
            listing = await resp.Content.ReadFromJsonAsync<Listing>(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Reddit search failed for {Ticker}.", symbol);
            return Array.Empty<SocialPost>();
        }

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

    private static string Compose(Post p)
    {
        var body = string.IsNullOrWhiteSpace(p.SelfText) ? p.Title! : $"{p.Title}. {p.SelfText}";
        return body!.Length > 1000 ? body[..1000] : body;
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && clock.UtcNow < _tokenExpiresUtc) return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.UtcNow < _tokenExpiresUtc) return _token;

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

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger?.LogWarning("Reddit token request failed: {Status}.", resp.StatusCode);
                return null;
            }

            var tok = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (tok is null || string.IsNullOrWhiteSpace(tok.AccessToken)) return null;

            _token = tok.AccessToken;
            // Refresh a minute early to avoid edge-of-expiry 401s.
            _tokenExpiresUtc = clock.UtcNow.AddSeconds(Math.Max(0, tok.ExpiresIn - 60));
            return _token;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Reddit token request failed.");
            return null;
        }
        finally { _tokenLock.Release(); }
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
