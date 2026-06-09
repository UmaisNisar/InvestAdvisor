namespace InvestAdvisor.Core.Entities;

/// <summary>
/// One insider transaction row, from Finnhub <c>/stock/insider-transactions</c> (SEC Form 4 sourced).
/// De-duplicated on (Ticker, Name, FilingDate, Change, TransactionCode).
/// </summary>
public class InsiderTrade
{
    public long Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Change { get; set; }   // signed share delta (negative = sale)
    public decimal Shares { get; set; }   // post-transaction holding
    public DateTime FilingDate { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public bool IsDerivative { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
