using InvestAdvisor.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.HostedServices;

/// <summary>
/// Optional auto-import: when <c>RuntimeSettings.HoldingsCsvPath</c> points at a holdings CSV
/// (e.g. a Wealthsimple export refreshed by the user's own automation), re-imports it whenever the
/// file changes — effectively a daily sync if the export refreshes daily. Does nothing when no path
/// is set; manual import via the Settings button works independently.
/// </summary>
public sealed class HoldingsImportWorker(
    IServiceProvider services,
    ILogger<HoldingsImportWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(3);
    private DateTime _lastImportedFileTimeUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TryImportAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Holdings auto-import tick failed."); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TryImportAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IRuntimeSettingsStore>().GetAsync(ct);
        var importer = scope.ServiceProvider.GetRequiredService<IHoldingsImportService>();

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
                    var result = await importer.ImportCsvAsync(content, ct);
                    _lastImportedFileTimeUtc = writeTime;
                    logger.LogInformation("Auto-imported holdings from file {Path}: {Added} added, {Updated} updated, {Skipped} skipped.",
                        path, result.Added, result.Updated, result.Skipped);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Could not import holdings file {Path}.", path); }
            }
        }

        // URL (e.g. a published Google Sheet that Wealthica syncs your holdings into).
        var url = settings.HoldingsCsvUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var result = await importer.ImportFromUrlAsync(url, ct);
            if (result.Errors.Count == 0)
                logger.LogInformation("Auto-imported holdings from URL: {Added} added, {Updated} updated, {Skipped} skipped.",
                    result.Added, result.Updated, result.Skipped);
            else
                logger.LogWarning("Holdings URL import had an issue: {Err}", result.Errors[0]);
        }
    }
}
