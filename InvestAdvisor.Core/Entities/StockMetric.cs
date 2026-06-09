namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A point-in-time fundamentals snapshot for one ticker, from Finnhub <c>/stock/metric</c>.
/// Headline numbers are extracted into typed columns; the full payload is kept in
/// <see cref="RawJson"/> so nothing fetched is lost.
/// </summary>
public class StockMetric
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal? MarketCap { get; set; }
    public decimal? PeRatio { get; set; }
    public decimal? RevenueGrowthPct { get; set; }
    public decimal? EpsGrowthPct { get; set; }
    public decimal? DebtToEquity { get; set; }
    /// <summary>Price / Free Cash Flow (TTM). Lower = cheaper vs. cash generation.
    /// Absolute FCF isn't on Finnhub's free tier, so this ratio is the usable proxy.</summary>
    public decimal? PriceToFreeCashFlow { get; set; }

    // Cross-asset momentum/risk (typed so the scorer reads them uniformly). For equities/ETFs:
    // 13-week and 26-week price returns + beta (Finnhub). For crypto: 7-day and 30-day returns
    // (CoinGecko); beta is null.
    public decimal? MomentumShort { get; set; }
    public decimal? MomentumLong { get; set; }
    public decimal? Beta { get; set; }

    public string RawJson { get; set; } = "{}";
}
