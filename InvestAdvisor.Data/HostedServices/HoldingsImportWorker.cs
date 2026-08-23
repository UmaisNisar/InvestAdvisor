using InvestAdvisor.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Optional auto-import: when <c>RuntimeSettings.HoldingsCsvPath</c> points at a holdings CSV
/// (e.g. a Wealthsimple export refreshed by the user's own automation), re-imports it whenever the
/// file changes — effectively a daily sync if the export refreshes daily. Does nothing when no path
/// is set; manual import via the Settings button works independently.
/// </summary>
public sealed class HoldingsImportWorker(IServiceProvider services, ILogger<HoldingsImportWorker> logger)
    : PeriodicWorker(services, logger, "Holdings import worker", TimeSpan.FromSeconds(20), TimeSpan.FromHours(3))
{
    private DateTime _lastImportedFileTimeUtc = DateTime.MinValue;

    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IRuntimeSettingsStore>().GetAsync(ct);
        var importer = scope.ServiceProvider.GetRequiredService<IHoldingsImportService>();

        // The global CSV auto-import targets the owner tenant (it predates multi-user; the
        // configured path/URL is a single source). Other tenants import via the UI.
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        int ownerTenantId;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var owner = await db.Tenants.AsNoTracking()
                .OrderByDescending(t => t.IsOwner).ThenBy(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (owner is null) return; // no tenants yet
            ownerTenantId = owner.Id;
        }

        // Local file: import only when it changes (avoids redundant writes).
        var path = settings.HoldingsCsvPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime > _lastImportedFileTimeUtc)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(path, ct);
                    var result = await importer.ImportCsvAsync(ownerTenantId, content, ct: ct);
                    _lastImportedFileTimeUtc = writeTime;
                    Logger.LogInformation("Auto-imported holdings from file {Path}: {Added} added, {Updated} updated, {Skipped} skipped.",
                        path, result.Added, result.Updated, result.Skipped);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logger.LogWarning(ex, "Could not import holdings file {Path}.", path); }
            }
        }

        // URL (e.g. a published Google Sheet that Wealthica syncs your holdings into).
        var url = settings.HoldingsCsvUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var result = await importer.ImportFromUrlAsync(ownerTenantId, url, ct: ct);
            if (result.Errors.Count == 0)
                Logger.LogInformation("Auto-imported holdings from URL: {Added} added, {Updated} updated, {Skipped} skipped.",
                    result.Added, result.Updated, result.Skipped);
            else
                Logger.LogWarning("Holdings URL import had an issue: {Err}", result.Errors[0]);
        }
    }
}
