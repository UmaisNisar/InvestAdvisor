using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class RealizedLotsService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant) : IRealizedLotsService
{
    public async Task<RealizedLot> CreateAsync(RealizedLot input, CancellationToken ct = default)
    {
        Validate(input);
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new RealizedLot
        {
            TenantId = tid,
            Ticker = input.Ticker.Trim().ToUpperInvariant(),
            Name = input.Name.Trim(),
            AssetClass = input.AssetClass,
            AccountType = input.AccountType,
            Quantity = input.Quantity,
            Proceeds = input.Proceeds,
            CostBasis = input.CostBasis,
            Currency = NormalizeCurrency(input.Currency),
            RealizedAtUtc = input.RealizedAtUtc == default ? DateTime.UtcNow : input.RealizedAtUtc,
            SourceHash = string.Empty, // hand-entered lots never collide with imported rows
            ManualEntry = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.RealizedLots.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<RealizedLot> UpdateAsync(int id, RealizedLot input, CancellationToken ct = default)
    {
        Validate(input);
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.RealizedLots.SingleOrDefaultAsync(l => l.Id == id && l.TenantId == tid, ct)
            ?? throw new InvalidOperationException($"Realized lot {id} not found.");
        entity.Ticker = input.Ticker.Trim().ToUpperInvariant();
        entity.Name = input.Name.Trim();
        entity.AssetClass = input.AssetClass;
        entity.AccountType = input.AccountType;
        entity.Quantity = input.Quantity;
        entity.Proceeds = input.Proceeds;
        entity.CostBasis = input.CostBasis;
        entity.Currency = NormalizeCurrency(input.Currency);
        entity.RealizedAtUtc = input.RealizedAtUtc == default ? entity.RealizedAtUtc : input.RealizedAtUtc;
        // SourceHash and ManualEntry are preserved so an edited imported lot still de-dupes on re-import.
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.RealizedLots.SingleOrDefaultAsync(l => l.Id == id && l.TenantId == tid, ct);
        if (entity is null) return;
        db.RealizedLots.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeCurrency(string? c) =>
        string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant();

    private static void Validate(RealizedLot l)
    {
        if (string.IsNullOrWhiteSpace(l.Ticker))
            throw new ArgumentException("Ticker is required.");
        if (string.IsNullOrWhiteSpace(l.Name))
            throw new ArgumentException("Name is required.");
        if (l.Quantity <= 0m)
            throw new ArgumentException("Quantity must be > 0.");
        if (l.Proceeds < 0m)
            throw new ArgumentException("Proceeds must be ≥ 0.");
        if (l.CostBasis < 0m)
            throw new ArgumentException("Cost basis must be ≥ 0.");
    }
}
