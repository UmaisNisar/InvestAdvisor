using System.Text.Json.Serialization;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// Finnhub /quote response. See https://finnhub.io/docs/api/quote.
/// </summary>
internal sealed class FinnhubQuoteResponse
{
    [JsonPropertyName("c")] public decimal Current { get; set; }
    [JsonPropertyName("d")] public decimal? Change { get; set; }
    [JsonPropertyName("dp")] public decimal? PercentChange { get; set; }
    [JsonPropertyName("h")] public decimal High { get; set; }
    [JsonPropertyName("l")] public decimal Low { get; set; }
    [JsonPropertyName("o")] public decimal Open { get; set; }
    [JsonPropertyName("pc")] public decimal PreviousClose { get; set; }
    [JsonPropertyName("t")] public long TimestampUnix { get; set; }
}

/// <summary>
/// Finnhub /crypto/candle response. See https://finnhub.io/docs/api/crypto-candles.
/// Used as a crypto fallback because /quote does not reliably return crypto prices.
/// </summary>
internal sealed class FinnhubCryptoCandleResponse
{
    [JsonPropertyName("c")] public decimal[]? Close { get; set; }
    [JsonPropertyName("h")] public decimal[]? High { get; set; }
    [JsonPropertyName("l")] public decimal[]? Low { get; set; }
    [JsonPropertyName("o")] public decimal[]? Open { get; set; }
    [JsonPropertyName("t")] public long[]? Timestamps { get; set; }
    [JsonPropertyName("v")] public decimal[]? Volume { get; set; }
    [JsonPropertyName("s")] public string? Status { get; set; }
}

/// <summary>Finnhub /company-news and /news item.</summary>
internal sealed class FinnhubNewsItem
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("headline")] public string? Headline { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("datetime")] public long DateTimeUnix { get; set; }
    [JsonPropertyName("related")] public string? Related { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
}
