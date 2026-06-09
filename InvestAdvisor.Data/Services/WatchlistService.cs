using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class WatchlistService(IDbContextFactory<InvestAdvisorDbContext> dbFactory) : IWatchlistService
{
    public async Task<IReadOnlyList<WatchlistItem>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WatchlistItems.AsNoTracking().OrderBy(w => w.Ticker).ToListAsync(ct);
    }

    public async Task<WatchlistItem> CreateAsync(WatchlistItem input, CancellationToken ct = default)
    {
        Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new WatchlistItem
        {
            Ticker = input.Ticker.Trim().ToUpperInvariant(),
            AssetClass = input.AssetClass,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            PriceTargetLow = input.PriceTargetLow,
            PriceTargetHigh = input.PriceTargetHigh,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.WatchlistItems.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<WatchlistItem> UpdateAsync(int id, WatchlistItem input, CancellationToken ct = default)
    {
        Validate(input);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.WatchlistItems.SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new InvalidOperationException($"WatchlistItem {id} not found.");
        entity.Ticker = input.Ticker.Trim().ToUpperInvariant();
        entity.AssetClass = input.AssetClass;
        entity.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        entity.PriceTargetLow = input.PriceTargetLow;
        entity.PriceTargetHigh = input.PriceTargetHigh;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.WatchlistItems.SingleOrDefaultAsync(w => w.Id == id, ct);
        if (entity is null) return;
        db.WatchlistItems.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(WatchlistItem w)
    {
        if (string.IsNullOrWhiteSpace(w.Ticker))
            throw new ArgumentException("Ticker is required.");
        if (w.PriceTargetLow is < 0m)
            throw new ArgumentException("PriceTargetLow must be ≥ 0.");
        if (w.PriceTargetHigh is < 0m)
            throw new ArgumentException("PriceTargetHigh must be ≥ 0.");
        if (w.PriceTargetLow is { } lo && w.PriceTargetHigh is { } hi && lo > hi)
            throw new ArgumentException("PriceTargetLow must be ≤ PriceTargetHigh.");
    }
}
