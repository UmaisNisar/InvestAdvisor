using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class HoldingsService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant,
    ISystemClock clock) : TenantScopedCrudService<Holding>(dbFactory, tenant, clock), IHoldingsService
{
    public Task<IReadOnlyList<Holding>> ListAsync(CancellationToken ct = default) => ListAsync(h => h.Ticker, ct);

    protected override DbSet<Holding> Set(InvestAdvisorDbContext db) => db.Holdings;

    protected override Holding NewEntity(Holding input, DateTime nowUtc)
    {
        var entity = new Holding { CreatedAtUtc = nowUtc };
        Apply(entity, input, nowUtc);
        return entity;
    }

    protected override void Apply(Holding entity, Holding input, DateTime nowUtc)
    {
        entity.Ticker = CleanTicker(input.Ticker);
        entity.Name = input.Name.Trim();
        entity.AssetClass = input.AssetClass;
        entity.Quantity = input.Quantity;
        entity.AvgCost = input.AvgCost;
        entity.Currency = Currency.Normalize(input.Currency);
        entity.AccountType = input.AccountType;
        entity.TargetAllocationPct = input.TargetAllocationPct;
        entity.Notes = CleanOptional(input.Notes);
        entity.UpdatedAtUtc = nowUtc;
    }

    protected override void Validate(Holding h)
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
