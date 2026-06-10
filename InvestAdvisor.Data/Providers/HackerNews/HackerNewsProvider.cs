using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.HackerNews;

/// <summary>
/// Searches Hacker News via the public Algolia API (no auth) for stories/comments mentioning a
/// ticker. Strong for tech/large-cap names; thinner elsewhere. Degrades to empty on any failure.
/// </summary>
public sealed class HackerNewsProvider(
    HttpClient http,
    IOptions<HackerNewsOptions> options,
    ILogger<HackerNewsProvider>? logger = null) : ISocialFeedProvider
{
    private const int HitsPerPage = 25;
    private readonly HackerNewsOptions _opts = options.Value;

    public NewsSource Channel => NewsSource.HackerNews;

    public async Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default)
    {
        if (!_opts.Enabled || string.IsNullOrWhiteSpace(ticker)) return Array.Empty<SocialPost>();

        var symbol = ticker.Trim().ToUpperInvariant();
        // Relative URL against the configured BaseAddress (set in DI / tests).
        var url = $"/api/v1/search_by_date?query={Uri.EscapeDataString(symbol)}" +
                  $"&tags={Uri.EscapeDataString("(story,comment)")}&hitsPerPage={HitsPerPage}";

        SearchResponse? payload;
        try
        {
            payload = await http.GetFromJsonAsync<SearchResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Hacker News search failed for {Ticker}.", symbol);
            return Array.Empty<SocialPost>();
        }

        if (payload?.Hits is null || payload.Hits.Length == 0) return Array.Empty<SocialPost>();

        return payload.Hits
            .Where(h => h is { ObjectId: not null } && h.Points >= _opts.MinPoints)
            .Select(h => (hit: h, text: TextOf(h)))
            .Where(x => !string.IsNullOrWhiteSpace(x.text))
            .Select(x => new SocialPost(
                Ticker: symbol,
                Text: Truncate(x.text!, 1000),
                Source: "Hacker News",
                Url: $"https://news.ycombinator.com/item?id={x.hit.ObjectId}",
                CreatedAtUtc: DateTimeOffset.FromUnixTimeSeconds(x.hit.CreatedAtI).UtcDateTime,
                Channel: NewsSource.HackerNews))
            .ToArray();
    }

    private static string? TextOf(Hit h) =>
        !string.IsNullOrWhiteSpace(h.Title) ? h.Title
        : !string.IsNullOrWhiteSpace(h.StoryText) ? h.StoryText
        : h.CommentText;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed class SearchResponse
    {
        [JsonPropertyName("hits")] public Hit[]? Hits { get; set; }
    }

    private sealed class Hit
    {
        [JsonPropertyName("objectID")] public string? ObjectId { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("story_text")] public string? StoryText { get; set; }
        [JsonPropertyName("comment_text")] public string? CommentText { get; set; }
        [JsonPropertyName("points")] public int? PointsRaw { get; set; }
        [JsonPropertyName("created_at_i")] public long CreatedAtI { get; set; }

        [JsonIgnore] public int Points => PointsRaw ?? 0;
    }
}
