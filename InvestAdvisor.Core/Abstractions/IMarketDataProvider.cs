using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IMarketDataProvider
{
    /// <summary>Returns a current quote for one ticker, or null if the provider has no data.</summary>
    Task<Quote?> GetQuoteAsync(string ticker, AssetClass assetClass, CancellationToken ct = default);
}
