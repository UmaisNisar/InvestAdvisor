namespace InvestAdvisor.Core.Models;

/// <summary>
/// A closed (sold) lot shown in the Realized gains list. <paramref name="RealizedPnl"/> is native
/// (Proceeds − CostBasis in <paramref name="Currency"/>); <paramref name="RealizedPnlUsd"/> is the
/// same converted to USD with current FX so totals line up with the rest of the dashboard.
/// </summary>
public sealed record RealizedLotView(
    int Id,
    string Ticker,
    string Name,
    string AssetClass,
    string AccountType,
    decimal Quantity,
    decimal Proceeds,
    decimal CostBasis,
    decimal RealizedPnl,
    decimal RealizedPnlUsd,
    decimal RealizedPnlPct,
    string Currency,
    DateTime RealizedAtUtc,
    bool ManualEntry);
