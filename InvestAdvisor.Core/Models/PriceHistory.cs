namespace InvestAdvisor.Core.Models;

/// <summary>One OHLCV bar for a single session. <see cref="Time"/> is the session date (UTC).</summary>
public sealed record Candle(
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

/// <summary>A ticker's historical bars, oldest first, plus the currency they're priced in.</summary>
public sealed record PriceHistory(
    string Ticker,
    string Currency,
    IReadOnlyList<Candle> Candles);
