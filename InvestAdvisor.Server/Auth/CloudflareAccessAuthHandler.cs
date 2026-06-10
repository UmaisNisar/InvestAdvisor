using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Server.Auth;

/// <summary>
/// Authenticates every request from the Cloudflare Access identity header
/// (<c>Cf-Access-Authenticated-User-Email</c>). The box is reachable only through the Cloudflare
/// tunnel, so the header is trustworthy. Falls back to a configured dev email
/// (<c>MultiTenancy:DevEmail</c>) when there is no header — so the app still works locally.
/// </summary>
public sealed class CloudflareAccessAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CloudflareAccess";
    private const string EmailHeader = "Cf-Access-Authenticated-User-Email";

    private readonly string? _devEmail;

    public CloudflareAccessAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _devEmail = config["MultiTenancy:DevEmail"];
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers[EmailHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(email))
            email = _devEmail;

        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult(AuthenticateResult.NoResult());

        email = email.Trim().ToLowerInvariant();
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.NameIdentifier, email),
        };
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
