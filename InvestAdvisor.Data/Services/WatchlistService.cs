using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class WatchlistService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant,
    ISystemClock clock) : TenantScopedCrudService<WatchlistItem>(dbFactory, tenant, clock), IWatchlistService
{
    public Task<IReadOnlyList<WatchlistItem>> ListAsync(CancellationToken ct = default) => ListAsync(w => w.Ticker, ct);

    protected override DbSet<WatchlistItem> Set(InvestAdvisorDbContext db) => db.WatchlistItems;

    protected override WatchlistItem NewEntity(WatchlistItem input, DateTime nowUtc)
    {
        var entity = new WatchlistItem { CreatedAtUtc = nowUtc };
        Apply(entity, input, nowUtc);
        return entity;
    }

    protected override void Apply(WatchlistItem entity, WatchlistItem input, DateTime nowUtc)
    {
        entity.Ticker = CleanTicker(input.Ticker);
        entity.AssetClass = input.AssetClass;
        entity.Note = CleanOptional(input.Note);
        entity.PriceTargetLow = input.PriceTargetLow;
        entity.PriceTargetHigh = input.PriceTargetHigh;
    }

    protected override void Validate(WatchlistItem w)
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
