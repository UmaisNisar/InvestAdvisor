using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.StockTwits;

/// <summary>
/// Reads the public StockTwits symbol stream (<c>/api/2/streams/symbol/{symbol}.json</c>). Each
/// message carries a body and often the author's own Bullish/Bearish tag, which we keep as a prior.
/// No key required for the public stream; an optional access token raises the rate limit. Degrades
/// to empty on any failure.
/// </summary>
public sealed class StockTwitsProvider(
    HttpClient http,
    IOptions<StockTwitsOptions> options,
    ILogger<StockTwitsProvider>? logger = null) : ISocialFeedProvider
{
    private const int MaxItems = 30;
    private readonly StockTwitsOptions _opts = options.Value;

    public NewsSource Channel => NewsSource.StockTwits;

    public async Task<IReadOnlyList<SocialPost>> GetTickerPostsAsync(string ticker, CancellationToken ct = default)
    {
        if (!_opts.Enabled || string.IsNullOrWhiteSpace(ticker)) return Array.Empty<SocialPost>();

        var symbol = ticker.Trim().ToUpperInvariant();
        var url = $"/api/2/streams/symbol/{Uri.EscapeDataString(symbol)}.json";
        if (!string.IsNullOrWhiteSpace(_opts.AccessToken))
            url += $"?access_token={Uri.EscapeDataString(_opts.AccessToken)}";

        StreamResponse? payload;
        try
        {
            payload = await http.GetFromJsonAsync<StreamResponse>(url, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "StockTwits stream failed for {Ticker}.", ticker);
            return Array.Empty<SocialPost>();
        }

        if (payload?.Messages is null || payload.Messages.Length == 0) return Array.Empty<SocialPost>();

        return payload.Messages
            .Where(m => m is { Id: > 0 } && !string.IsNullOrWhiteSpace(m.Body))
            .Take(MaxItems)
            .Select(m => new SocialPost(
                Ticker: symbol,
                Text: m.Body!,
                Source: "StockTwits",
                Url: PermalinkFor(m),
                CreatedAtUtc: m.CreatedAt.UtcDateTime,
                Channel: NewsSource.StockTwits,
                ProviderSentiment: m.Entities?.Sentiment?.Basic))
            .ToArray();
    }

    private static string PermalinkFor(Message m)
    {
        var user = m.User?.Username;
        return string.IsNullOrWhiteSpace(user)
            ? $"https://stocktwits.com/message/{m.Id}"
            : $"https://stocktwits.com/{user}/message/{m.Id}";
    }

    private sealed record StreamResponse(
        [property: JsonPropertyName("messages")] Message[]? Messages);

    private sealed record Message(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("user")] StUser? User,
        [property: JsonPropertyName("entities")] StEntities? Entities);

    private sealed record StUser([property: JsonPropertyName("username")] string? Username);

    private sealed record StEntities([property: JsonPropertyName("sentiment")] StSentiment? Sentiment);

    private sealed record StSentiment([property: JsonPropertyName("basic")] string? Basic);
}
