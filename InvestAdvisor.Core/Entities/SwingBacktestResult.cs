namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A persisted snapshot of the swing rule's backtest over the universe's history. The UI surfaces
/// the latest row as the evidence gate: until it shows a positive edge on a meaningful sample, live
/// setups stay labelled "unvalidated — paper only".
/// </summary>
public class SwingBacktestResult
{
    public long Id { get; set; }
    public DateTime GeneratedAtUtc { get; set; }

    public int TotalTrades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public decimal WinRatePct { get; set; }
    public decimal AverageR { get; set; }
    public decimal ExpectancyR { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdownR { get; set; }
    public decimal AverageHoldingDays { get; set; }

    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
