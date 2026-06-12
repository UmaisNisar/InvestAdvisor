using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace InvestAdvisor.Maui.HostServices;

/// <summary>
/// Identity seam for the desktop app: there is no auth layer, so every request belongs to one
/// local user. TenantContext auto-provisions the tenant + default profile on first run. The
/// email is configurable (LocalUser:Email) so a future server sync can match the same account
/// the user logs into on the web app.
/// </summary>
public sealed class LocalUserAccessor(IConfiguration configuration) : ICurrentUserAccessor
{
    private const string DefaultEmail = "local@desktop";

    public Task<string?> GetEmailAsync(CancellationToken ct = default)
        => Task.FromResult<string?>(configuration.GetValue("LocalUser:Email", DefaultEmail));
}
