using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IPriceHistoryProvider
{
    /// <summary>Returns daily OHLC bars for one ticker over the given window, or null if unavailable.</summary>
    Task<PriceHistory?> GetHistoryAsync(string ticker, AssetClass assetClass, HistoryRange range, CancellationToken ct = default);
}
