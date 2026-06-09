using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Models;

/// <summary>Provider-shaped quote for one ticker. Always quoted in USD in v1.</summary>
public sealed record Quote(
    string Ticker,
    AssetClass AssetClass,
    decimal Price,
    decimal PreviousClose,
    decimal PercentChange,
    DateTime FetchedAtUtc);
