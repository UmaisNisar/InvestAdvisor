using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class RealizedLotsService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant,
    ISystemClock clock) : TenantScopedCrudService<RealizedLot>(dbFactory, tenant, clock), IRealizedLotsService
{
    protected override DbSet<RealizedLot> Set(InvestAdvisorDbContext db) => db.RealizedLots;

    protected override string NotFoundMessage(int id) => $"Realized lot {id} not found.";

    protected override RealizedLot NewEntity(RealizedLot input, DateTime nowUtc)
    {
        var entity = new RealizedLot
        {
            RealizedAtUtc = nowUtc,
            SourceHash = string.Empty, // hand-entered lots never collide with imported rows
            ManualEntry = true,
            CreatedAtUtc = nowUtc,
        };
        Apply(entity, input, nowUtc);
        return entity;
    }

    // SourceHash and ManualEntry are preserved on update so an edited imported lot still de-dupes on re-import.
    protected override void Apply(RealizedLot entity, RealizedLot input, DateTime nowUtc)
    {
        entity.Ticker = CleanTicker(input.Ticker);
        entity.Name = input.Name.Trim();
        entity.AssetClass = input.AssetClass;
        entity.AccountType = input.AccountType;
        entity.Quantity = input.Quantity;
        entity.Proceeds = input.Proceeds;
        entity.CostBasis = input.CostBasis;
        entity.Currency = Currency.Normalize(input.Currency);
        if (input.RealizedAtUtc != default) entity.RealizedAtUtc = input.RealizedAtUtc;
    }

    protected override void Validate(RealizedLot l)
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
