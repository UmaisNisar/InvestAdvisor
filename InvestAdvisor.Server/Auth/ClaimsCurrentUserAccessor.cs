using System.Security.Claims;
using InvestAdvisor.Core.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;

namespace InvestAdvisor.Server.Auth;

/// <summary>
/// Reads the current user's email from the Blazor authentication state (populated by the
/// Cloudflare Access auth handler and flowed into each circuit). The swappable identity source
/// behind <see cref="ICurrentUserAccessor"/>.
/// </summary>
public sealed class ClaimsCurrentUserAccessor(AuthenticationStateProvider authState) : ICurrentUserAccessor
{
    public async Task<string?> GetEmailAsync(CancellationToken ct = default)
    {
        var state = await authState.GetAuthenticationStateAsync();
        var user = state.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        return user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value
            ?? user.Identity.Name;
    }
}
