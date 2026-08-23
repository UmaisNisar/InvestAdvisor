using InvestAdvisor.Core.Trading;

namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A persisted snapshot of a strategy's backtest over its universe's history. The UI surfaces the
/// latest row per strategy as the evidence gate: until it shows a positive edge on a meaningful
/// sample, that strategy's live setups stay labelled "unvalidated — paper only".
/// </summary>
public class BacktestResult
{
    public long Id { get; set; }
    public StrategyKind Strategy { get; set; }
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

    public BacktestSummary ToSummary() => new(TotalTrades, Wins, Losses, WinRatePct, AverageR, ExpectancyR,
        ProfitFactor, MaxDrawdownR, AverageHoldingDays, FromUtc, ToUtc);

    public static BacktestResult From(StrategyKind strategy, BacktestSummary s, DateTime nowUtc) => new()
    {
        Strategy = strategy,
        GeneratedAtUtc = nowUtc,
        TotalTrades = s.TotalTrades,
        Wins = s.Wins,
        Losses = s.Losses,
        WinRatePct = s.WinRatePct,
        AverageR = s.AverageR,
        ExpectancyR = s.ExpectancyR,
        ProfitFactor = s.ProfitFactor,
        MaxDrawdownR = s.MaxDrawdownR,
        AverageHoldingDays = s.AverageHoldingDays,
        FromUtc = s.FromUtc,
        ToUtc = s.ToUtc,
    };
}
