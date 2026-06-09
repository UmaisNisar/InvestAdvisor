namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Supplies the identity of the person making the current request. The implementation is the
/// swappable auth seam: today it reads the Cloudflare Access email; later it can read an
/// app-owned login. Returns null when there is no authenticated caller (e.g. background workers,
/// which scope by an explicit tenant instead).
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>The authenticated email, or null if there is no authenticated caller.</summary>
    Task<string?> GetEmailAsync(CancellationToken ct = default);
}
