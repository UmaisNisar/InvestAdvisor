using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data.Identity;

/// <summary>
/// Resolves the current request's tenant from the authenticated email, provisioning the tenant +
/// a default profile on first login. Scoped (cached for the request/circuit).
/// </summary>
public sealed class TenantContext(
    ICurrentUserAccessor currentUser,
    IDbContextFactory<InvestAdvisorDbContext> dbFactory) : ITenantContext
{
    private TenantInfo? _cached;

    public async Task<int> GetTenantIdAsync(CancellationToken ct = default)
        => (await GetCurrentAsync(ct)).Id;

    public async Task<TenantInfo> GetCurrentAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var email = (await currentUser.GetEmailAsync(ct))?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("No authenticated user on the current request.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Email == email, ct);
        if (tenant is null)
        {
            tenant = new Tenant { Email = email, DisplayName = email, CreatedAtUtc = DateTime.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct); // assigns tenant.Id

            // First-login provisioning: a default profile so the new tenant has a working portfolio.
            db.Profiles.Add(new Profile
            {
                TenantId = tenant.Id,
                GoalsText = "Long-term growth with disciplined rebalancing.",
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        _cached = new TenantInfo(tenant.Id, tenant.Email, tenant.DisplayName);
        return _cached;
    }
}
