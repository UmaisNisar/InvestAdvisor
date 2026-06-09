using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

public interface IPortfolioQueries
{
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken ct = default);
    Task<AdvicePage> GetAdvicePageAsync(int skip, int take, CancellationToken ct = default);
    Task<AdviceLogDetailView?> GetAdviceDetailAsync(long id, CancellationToken ct = default);
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}
