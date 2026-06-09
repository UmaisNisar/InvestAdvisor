namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Reads screener inputs for a single ticker: fundamentals, the latest analyst recommendation
/// distribution, and recent insider transactions. Implementations are rate-limited and return
/// null / empty on missing or premium-gated data rather than throwing.
/// </summary>
public interface IScreenerDataProvider
{
    Task<FundamentalsResult?> GetFundamentalsAsync(string ticker, CancellationToken ct = default);
    Task<AnalystRatingResult?> GetLatestAnalystRatingAsync(string ticker, CancellationToken ct = default);
    Task<IReadOnlyList<InsiderTradeResult>> GetInsiderTradesAsync(string ticker, CancellationToken ct = default);
}

public sealed record FundamentalsResult(
    decimal? MarketCap,
    decimal? PeRatio,
    decimal? RevenueGrowthPct,
    decimal? EpsGrowthPct,
    decimal? DebtToEquity,
    decimal? PriceToFreeCashFlow,
    decimal? MomentumShort,
    decimal? MomentumLong,
    decimal? Beta,
    string RawJson);

public sealed record AnalystRatingResult(
    string Period,
    int StrongBuy,
    int Buy,
    int Hold,
    int Sell,
    int StrongSell);

public sealed record InsiderTradeResult(
    string Name,
    decimal Change,
    decimal Shares,
    DateTime FilingDate,
    string TransactionCode,
    bool IsDerivative);
