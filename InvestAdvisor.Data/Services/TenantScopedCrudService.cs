using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Create / update / delete for a tenant-owned table, with the tenant resolved from the current
/// user on every call so a row can never be read or written across tenants. Subclasses supply
/// the validation rule and the two entity mappings; everything else (scoping, lookup, save) is here.
/// </summary>
public abstract class TenantScopedCrudService<TEntity>(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant,
    ISystemClock clock)
    where TEntity : class, ITenantOwned
{
    protected abstract DbSet<TEntity> Set(InvestAdvisorDbContext db);

    /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="input"/> can't be saved.</summary>
    protected abstract void Validate(TEntity input);

    /// <summary>A fresh row from user input; the base stamps <c>TenantId</c> afterwards.</summary>
    protected abstract TEntity NewEntity(TEntity input, DateTime nowUtc);

    /// <summary>Copies the editable fields of <paramref name="input"/> onto the tracked row.</summary>
    protected abstract void Apply(TEntity target, TEntity input, DateTime nowUtc);

    protected virtual string NotFoundMessage(int id) => $"{typeof(TEntity).Name} {id} not found.";

    protected async Task<IReadOnlyList<TEntity>> ListAsync<TKey>(
        Func<TEntity, TKey> orderBy, CancellationToken ct)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await Set(db).AsNoTracking().Where(e => e.TenantId == tid).ToListAsync(ct);
        return rows.OrderBy(orderBy).ToList();
    }

    public async Task<TEntity> CreateAsync(TEntity input, CancellationToken ct = default)
    {
        Validate(input);
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = NewEntity(input, clock.UtcNow);
        entity.TenantId = tid;
        Set(db).Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<TEntity> UpdateAsync(int id, TEntity input, CancellationToken ct = default)
    {
        Validate(input);
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await Set(db).SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tid, ct)
            ?? throw new InvalidOperationException(NotFoundMessage(id));
        Apply(entity, input, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await Set(db).SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tid, ct);
        if (entity is null) return;
        Set(db).Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    protected static string CleanTicker(string ticker) => ticker.Trim().ToUpperInvariant();
    protected static string? CleanOptional(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
