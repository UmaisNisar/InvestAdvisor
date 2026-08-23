using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestAdvisor.Data.Composition;

/// <summary>
/// The two host-level steps both heads (Blazor Server and MAUI) perform identically: which
/// background workers to register, and migrating the database before anything reads it.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// Registers the background workers. The credit-spending set (agent loop, screener/daily
    /// recommendation, strategy scans) is gated on <c>Scheduler:WorkerEnabled</c>, defaulting to
    /// <paramref name="workersEnabledByDefault"/>; the holdings importer has no LLM cost and
    /// always runs.
    /// </summary>
    public static IServiceCollection AddInvestAdvisorWorkers(
        this IServiceCollection services, IConfiguration configuration, bool workersEnabledByDefault)
    {
        if (configuration.GetValue(SchedulerOptions.WorkerEnabledKey, workersEnabledByDefault))
        {
            services.AddHostedService<InvestAdvisorWorker>();
            services.AddHostedService<ScreenerWorker>();
            services.AddHostedService<SwingWorker>();
            services.AddHostedService<MomentumWorker>();
        }
        services.AddHostedService<HoldingsImportWorker>();
        return services;
    }

    /// <summary>Applies pending EF migrations. Call once at startup before the workers tick.</summary>
    public static async Task MigrateInvestAdvisorAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
