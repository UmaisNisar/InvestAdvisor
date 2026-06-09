using InvestAdvisor.Data;
using InvestAdvisor.Data.Composition;
using InvestAdvisor.Data.HostedServices;
using InvestAdvisor.Server.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

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

// The AdviceDetailDrawer streams raw response JSON over the circuit, so raise the
// SignalR receive cap (applies to the Blazor component hub via the global default).
builder.Services.Configure<HubOptions>(o => o.MaximumReceiveMessageSize = 1024 * 1024);

builder.Services.AddHostedService<InvestAdvisorWorker>();
builder.Services.AddHostedService<ScreenerWorker>();
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // Routable pages (@page) live in the shared InvestAdvisor.Ui RCL, not this host
    // assembly, so register it for endpoint discovery.
    .AddAdditionalAssemblies(typeof(InvestAdvisor.Ui.Root).Assembly);

app.Run();
