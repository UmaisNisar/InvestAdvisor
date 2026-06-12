using InvestAdvisor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Maui.HostServices;

/// <summary>
/// MAUI has no generic host, so the background engine (DB migration + the registered
/// IHostedServices) is started explicitly when the window opens and stopped when it closes —
/// the same lifecycle the Photino host drove around Photino's Run().
/// </summary>
public sealed class EngineRunner(IServiceProvider services, ILogger<EngineRunner> logger)
{
    private readonly CancellationTokenSource _cts = new();
    private IHostedService[] _started = [];

    public async Task StartAsync()
    {
        try
        {
            await using (var scope = services.CreateAsyncScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
                await using var db = await factory.CreateDbContextAsync(_cts.Token);
                await db.Database.MigrateAsync(_cts.Token);
            }

            var hostedServices = services.GetServices<IHostedService>().ToArray();
            foreach (var svc in hostedServices)
                await svc.StartAsync(_cts.Token);
            _started = hostedServices;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background engine failed to start.");
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        foreach (var svc in _started)
        {
            try { await svc.StopAsync(CancellationToken.None); }
            catch { /* swallow on shutdown */ }
        }
        _started = [];
    }
}
