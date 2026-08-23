using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using FluentValidation;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Validation;
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

    protected override IValidator<Holding> Validator { get; } = new HoldingValidator();
}
