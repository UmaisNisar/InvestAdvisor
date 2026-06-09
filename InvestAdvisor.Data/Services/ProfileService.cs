using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Services;

public sealed class ProfileService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ITenantContext tenant) : IProfileService
{
    public async Task<Profile> GetAsync(CancellationToken ct = default)
    {
        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Profiles.AsNoTracking().SingleAsync(p => p.TenantId == tid, ct);
    }

    public async Task<Profile> UpdateAsync(Profile updated, CancellationToken ct = default)
    {
        if (updated.DriftPctThreshold < 0m || updated.DriftPctThreshold > 100m)
            throw new ArgumentException("DriftPctThreshold must be 0..100.");
        if (updated.SingleDayMovePctThreshold < 0m || updated.SingleDayMovePctThreshold > 100m)
            throw new ArgumentException("SingleDayMovePctThreshold must be 0..100.");
        if (updated.RebalanceCadenceHours < 1)
            throw new ArgumentException("RebalanceCadenceHours must be ≥ 1.");

        var tid = await tenant.GetTenantIdAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Profiles.SingleAsync(p => p.TenantId == tid, ct);
        row.GoalsText = updated.GoalsText ?? string.Empty;
        row.RiskTolerance = updated.RiskTolerance;
        row.TimeHorizon = updated.TimeHorizon;
        row.DriftPctThreshold = updated.DriftPctThreshold;
        row.SingleDayMovePctThreshold = updated.SingleDayMovePctThreshold;
        row.RebalanceCadenceHours = updated.RebalanceCadenceHours;
        row.SystemPromptOverride = string.IsNullOrWhiteSpace(updated.SystemPromptOverride)
            ? null : updated.SystemPromptOverride.Trim();
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return row;
    }
}
