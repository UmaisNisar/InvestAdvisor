using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Abstractions;

public interface IWatchlistService
{
    Task<IReadOnlyList<WatchlistItem>> ListAsync(CancellationToken ct = default);
    Task<WatchlistItem> CreateAsync(WatchlistItem input, CancellationToken ct = default);
    Task<WatchlistItem> UpdateAsync(int id, WatchlistItem input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
