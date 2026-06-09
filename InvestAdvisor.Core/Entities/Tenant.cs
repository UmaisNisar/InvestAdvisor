namespace InvestAdvisor.Core.Entities;

/// <summary>
/// A tenant = one isolated data boundary (its own holdings, watchlist, profile, advice).
/// The auth layer (Cloudflare Access email today; app-owned auth later) maps a logged-in person
/// to their tenant. Provisioned on first login. One person ≈ one tenant for now; a tenant could
/// gain multiple member logins when the app opens to the public.
/// </summary>
public class Tenant
{
    public int Id { get; set; }
    /// <summary>The login email that identifies this tenant (the Cloudflare Access identity).</summary>
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>The first/owner tenant — inherits the pre-multi-tenant data in the migration.</summary>
    public bool IsOwner { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
