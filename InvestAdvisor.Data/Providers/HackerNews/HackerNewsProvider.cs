using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Core.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.HackerNews;

/// <summary>
/// Searches Hacker News via the public Algolia API (no auth) for stories/comments mentioning a
/// ticker. Strong for tech/large-cap names; thinner elsewhere.
/// </summary>
public sealed class HackerNewsProvider(
    HttpClient http,
    IOptions<HackerNewsOptions> options,
    ILogger<HackerNewsProvider>? logger = null) : SocialFeedProviderBase(logger, "Hacker News")
{
    private const int HitsPerPage = 25;
    private readonly HackerNewsOptions _opts = options.Value;

    public override NewsSource Channel => NewsSource.HackerNews;
    protected override bool Enabled => _opts.Enabled;

    protected override async Task<IReadOnlyList<SocialPost>> FetchAsync(string symbol, CancellationToken ct)
    {
        // Relative URL against the configured BaseAddress (set in DI / tests).
        var url = $"/api/v1/search_by_date?query={Uri.EscapeDataString(symbol)}" +
                  $"&tags={Uri.EscapeDataString("(story,comment)")}&hitsPerPage={HitsPerPage}";

        var payload = await http.GetFromJsonAsync<SearchResponse>(url, ct);
        if (payload?.Hits is null || payload.Hits.Length == 0) return Array.Empty<SocialPost>();

        return payload.Hits
            .Where(h => h is { ObjectId: not null } && h.Points >= _opts.MinPoints)
            .Select(h => (hit: h, text: TextOf(h)))
            .Where(x => !string.IsNullOrWhiteSpace(x.text))
            .Select(x => new SocialPost(
                Ticker: symbol,
                Text: Strings.Truncate(x.text!, 1000),
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
