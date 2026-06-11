namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Imports holdings from a CSV (e.g. a Wealthsimple export). Columns are matched flexibly by
/// header name; rows are upserted into the portfolio by ticker + account. No credentials involved.
/// </summary>
public interface IHoldingsImportService
{
    /// <param name="replaceExisting">When true the file is treated as the full portfolio: holdings
    /// not present in it are removed after the upsert. Skipped entirely if no row imported, so a
    /// wrong/empty file can never wipe the portfolio.</param>
    Task<HoldingsImportResult> ImportCsvAsync(int tenantId, string csvContent, bool replaceExisting = false, CancellationToken ct = default);

    /// <summary>Fetches a CSV from a URL (e.g. a published Google Sheet) and imports it.</summary>
    Task<HoldingsImportResult> ImportFromUrlAsync(int tenantId, string url, bool replaceExisting = false, CancellationToken ct = default);
}

public sealed record HoldingsImportResult(
    int Added,
    int Updated,
    int Skipped,
    IReadOnlyList<string> Errors,
    int Removed = 0)
{
    public bool AnyChanges => Added > 0 || Updated > 0 || Removed > 0;
    public int RowsProcessed => Added + Updated;
}
