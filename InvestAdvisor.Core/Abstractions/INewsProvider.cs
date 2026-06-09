using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface INewsProvider
{
    /// <summary>Recent headlines for one ticker; provider decides the lookback window (typically 24-72h).</summary>
    Task<IReadOnlyList<NewsHeadline>> GetTickerNewsAsync(string ticker, CancellationToken ct = default);

    /// <summary>Recent market-wide / general business headlines.</summary>
    Task<IReadOnlyList<NewsHeadline>> GetMarketNewsAsync(CancellationToken ct = default);
}
