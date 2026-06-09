using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Type-ahead symbol search for the "add holding" picker. Merges the curated screener universe
/// (instant, with a known asset class) with Finnhub's broad symbol search.
/// </summary>
public interface ITickerSearchService
{
    Task<IReadOnlyList<TickerSearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public sealed record TickerSearchResult(string Symbol, string Name, AssetClass AssetClass);
