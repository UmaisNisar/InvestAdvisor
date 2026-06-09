using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Abstractions;

public interface IRuntimeSettingsStore
{
    /// <summary>Returns the current settings snapshot, loading from DB on first call and on invalidation.</summary>
    ValueTask<RuntimeSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Persists changes to the singleton row and invalidates the cached snapshot.</summary>
    Task UpdateAsync(Action<RuntimeSettings> mutate, CancellationToken ct = default);
}
