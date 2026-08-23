using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using FluentValidation;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Validation;
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

    protected override IValidator<RealizedLot> Validator { get; } = new RealizedLotValidator();
}
