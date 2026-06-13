using System.Globalization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Data;
using InvestAdvisor.Data.Composition;
using InvestAdvisor.Data.HostedServices;
using InvestAdvisor.Server.Auth;
using InvestAdvisor.Server.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

// Pin the app to en-US so currency/number formatting is consistent regardless of the host's
// locale. On a minimal Linux box LANG defaults to C.UTF-8 (the invariant culture), which makes
// ToString("C") render the generic "¤" currency sign instead of "$". All displayed dollar
// figures are USD, so en-US is the right fixed culture.
var appCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

var builder = WebApplication.CreateBuilder(args);

// Configuration: appsettings + user-secrets (dev) + env vars + friendly env aliases.
builder.Configuration.AddInvestAdvisorEnvAliases();

// Bind Kestrel to loopback only — Cloudflare Tunnel terminates TLS and forwards locally.
// Override via ASPNETCORE_URLS env var if you want a different binding.
builder.WebHost.ConfigureKestrel((ctx, opts) =>
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        opts.ListenLocalhost(5174);
});

builder.Services.AddInvestAdvisor(builder.Configuration);
builder.Services.AddMudServices();

// Blazor Web App, interactive Server render mode. This replaces the legacy
// AddServerSideBlazor + MapBlazorHub + _Host.cshtml model, which on .NET 10 no longer
// serves the client script (blazor.server.js) and so left the app non-interactive.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Multi-tenancy identity: Cloudflare Access supplies the email (dev fallback configured), which
// flows into each circuit and is resolved to a tenant by ITenantContext.
builder.Services.AddAuthentication(CloudflareAccessAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, CloudflareAccessAuthHandler>(CloudflareAccessAuthHandler.SchemeName, null);
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ICurrentUserAccessor, ClaimsCurrentUserAccessor>();

// The AdviceDetailDrawer streams raw response JSON over the circuit, so raise the
// SignalR receive cap (applies to the Blazor component hub via the global default).
builder.Services.Configure<HubOptions>(o => o.MaximumReceiveMessageSize = 1024 * 1024);

// The agent loop + screener spend Anthropic credits, so they run only when enabled. The flag
// defaults to "on outside Development" — a local `dotnet run` won't burn credits unless you set
// Scheduler:WorkerEnabled=true. The holdings importer has no LLM cost, so it always runs.
var workersEnabled = builder.Configuration.GetValue(
    $"{InvestAdvisor.Core.Options.SchedulerOptions.SectionName}:WorkerEnabled",
    !builder.Environment.IsDevelopment());
if (workersEnabled)
{
    builder.Services.AddHostedService<InvestAdvisorWorker>();
    builder.Services.AddHostedService<ScreenerWorker>();
    builder.Services.AddHostedService<SwingWorker>();
}
builder.Services.AddHostedService<HoldingsImportWorker>();

var app = builder.Build();

// Migrate on startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // Routable pages (@page) live in the shared InvestAdvisor.Ui RCL, not this host
    // assembly, so register it for endpoint discovery.
    .AddAdditionalAssemblies(typeof(InvestAdvisor.Ui.Root).Assembly);

app.Run();
