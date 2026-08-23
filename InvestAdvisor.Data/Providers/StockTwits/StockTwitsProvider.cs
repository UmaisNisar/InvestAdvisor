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
/// No key required for the public stream; an optional access token raises the rate limit.
/// </summary>
public sealed class StockTwitsProvider(
    HttpClient http,
    IOptions<StockTwitsOptions> options,
    ILogger<StockTwitsProvider>? logger = null) : SocialFeedProviderBase(logger, "StockTwits")
{
    private const int MaxItems = 30;
    private readonly StockTwitsOptions _opts = options.Value;

    public override NewsSource Channel => NewsSource.StockTwits;
    protected override bool Enabled => _opts.Enabled;

    protected override async Task<IReadOnlyList<SocialPost>> FetchAsync(string symbol, CancellationToken ct)
    {
        var url = $"/api/2/streams/symbol/{Uri.EscapeDataString(symbol)}.json";
        if (!string.IsNullOrWhiteSpace(_opts.AccessToken))
            url += $"?access_token={Uri.EscapeDataString(_opts.AccessToken)}";

        var payload = await http.GetFromJsonAsync<StreamResponse>(url, ct);
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
