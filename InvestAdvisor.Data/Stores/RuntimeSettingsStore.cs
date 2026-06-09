using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Stores;

/// <summary>
/// In-memory-cached store for the singleton <see cref="RuntimeSettings"/> row.
/// The cache is invalidated whenever <see cref="UpdateAsync"/> writes through it; outside callers
/// (e.g. the worker tick) call <see cref="GetAsync"/> each iteration, so the cache is read-mostly.
/// </summary>
public sealed class RuntimeSettingsStore(IDbContextFactory<InvestAdvisorDbContext> dbFactory) : IRuntimeSettingsStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RuntimeSettings? _cached;

    public async ValueTask<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        var cached = _cached;
        if (cached is not null) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            _cached = await db.RuntimeSettings.AsNoTracking()
                .SingleAsync(r => r.Id == RuntimeSettings.SingletonId, ct);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<RuntimeSettings> mutate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.RuntimeSettings.SingleAsync(
                r => r.Id == RuntimeSettings.SingletonId, ct);
            mutate(row);
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _cached = await db.RuntimeSettings.AsNoTracking()
                .SingleAsync(r => r.Id == RuntimeSettings.SingletonId, ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
