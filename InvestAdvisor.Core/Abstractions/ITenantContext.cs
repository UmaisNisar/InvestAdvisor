namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Resolves the current request's tenant (the data boundary). For UI requests it maps the
/// authenticated email to a tenant, provisioning the tenant + a default profile on first login.
/// Background workers don't use this — they iterate tenants and pass an explicit id.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant id, provisioning on first login. Throws if no authenticated caller.</summary>
    Task<int> GetTenantIdAsync(CancellationToken ct = default);

    Task<TenantInfo> GetCurrentAsync(CancellationToken ct = default);
}

public sealed record TenantInfo(int Id, string Email, string DisplayName);
