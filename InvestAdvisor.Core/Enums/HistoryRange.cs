namespace InvestAdvisor.Core.Enums;

/// <summary>Lookback window for a price-history chart.</summary>
public enum HistoryRange
{
    /// <summary>Most recent trading session, intraday (5-minute) bars.</summary>
    OneDay,
    /// <summary>Last five trading sessions, hourly bars.</summary>
    OneWeek,
    OneMonth,
    ThreeMonths,
    SixMonths,
    OneYear,
}
