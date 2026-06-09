using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Abstractions;

public interface IHoldingsService
{
    Task<IReadOnlyList<Holding>> ListAsync(CancellationToken ct = default);
    Task<Holding> CreateAsync(Holding input, CancellationToken ct = default);
    Task<Holding> UpdateAsync(int id, Holding input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
