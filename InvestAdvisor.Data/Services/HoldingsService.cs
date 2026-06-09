using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class HoldingsService(IDbContextFactory<InvestAdvisorDbContext> dbFactory) : IHoldingsService
{
    public async Task<IReadOnlyList<Holding>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Holdings.AsNoTracking().OrderBy(h => h.Ticker).ToListAsync(ct);
    }

    public async Task<Holding> CreateAsync(Holding input, CancellationToken ct = default)
    {
        Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new Holding
        {
            Ticker = input.Ticker.Trim().ToUpperInvariant(),
            Name = input.Name.Trim(),
            AssetClass = input.AssetClass,
            Quantity = input.Quantity,
            AvgCost = input.AvgCost,
            Currency = NormalizeCurrency(input.Currency),
            AccountType = input.AccountType,
            TargetAllocationPct = input.TargetAllocationPct,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.Holdings.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<Holding> UpdateAsync(int id, Holding input, CancellationToken ct = default)
    {
        Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Holdings.SingleOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new InvalidOperationException($"Holding {id} not found.");
        entity.Ticker = input.Ticker.Trim().ToUpperInvariant();
        entity.Name = input.Name.Trim();
        entity.AssetClass = input.AssetClass;
        entity.Quantity = input.Quantity;
        entity.AvgCost = input.AvgCost;
        entity.Currency = NormalizeCurrency(input.Currency);
        entity.AccountType = input.AccountType;
        entity.TargetAllocationPct = input.TargetAllocationPct;
        entity.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Holdings.SingleOrDefaultAsync(h => h.Id == id, ct);
        if (entity is null) return;
        db.Holdings.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeCurrency(string? c) =>
        string.IsNullOrWhiteSpace(c) ? "USD" : c.Trim().ToUpperInvariant();

    private static void Validate(Holding h)
    {
        if (string.IsNullOrWhiteSpace(h.Ticker))
            throw new ArgumentException("Ticker is required.");
        if (string.IsNullOrWhiteSpace(h.Name))
            throw new ArgumentException("Name is required.");
        if (h.Quantity < 0m)
            throw new ArgumentException("Quantity must be ≥ 0.");
        if (h.AvgCost < 0m)
            throw new ArgumentException("AvgCost must be ≥ 0.");
        if (h.TargetAllocationPct is < 0m or > 100m)
            throw new ArgumentException("TargetAllocationPct must be between 0 and 100.");
    }
}
