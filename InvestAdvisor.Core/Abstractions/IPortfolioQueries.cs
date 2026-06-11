using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IPortfolioQueries
{
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken ct = default);

    /// <summary>
    /// The portfolio's market value over <paramref name="range"/>, reconstructed from per-ticker
    /// price history (today's share counts and FX rates held constant). Live provider calls — one
    /// per distinct ticker — so callers should treat it as a slow read.
    /// </summary>
    Task<PortfolioValueHistory> GetValueHistoryAsync(HistoryRange range, CancellationToken ct = default);

    Task<AdvicePage> GetAdvicePageAsync(int skip, int take, CancellationToken ct = default);
    Task<AdviceLogDetailView?> GetAdviceDetailAsync(long id, CancellationToken ct = default);
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}
