namespace InvestAdvisor.Core.Entities;

/// <summary>
/// Analyst recommendation distribution for one ticker for a given month, from Finnhub
/// <c>/stock/recommendation</c>. One row per (Ticker, Period); month-over-month deltas drive
/// the "biggest analyst shifts" signal.
/// </summary>
public class AnalystRating
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty; // e.g. "2026-06-01"
    public int StrongBuy { get; set; }
    public int Buy { get; set; }
    public int Hold { get; set; }
    public int Sell { get; set; }
    public int StrongSell { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
