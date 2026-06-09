namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Imports holdings from a CSV (e.g. a Wealthsimple export). Columns are matched flexibly by
/// header name; rows are upserted into the portfolio by ticker + account. No credentials involved.
/// </summary>
public interface IHoldingsImportService
{
    Task<HoldingsImportResult> ImportCsvAsync(string csvContent, CancellationToken ct = default);

    /// <summary>Fetches a CSV from a URL (e.g. a published Google Sheet) and imports it.</summary>
    Task<HoldingsImportResult> ImportFromUrlAsync(string url, CancellationToken ct = default);
}

public sealed record HoldingsImportResult(
    int Added,
    int Updated,
    int Skipped,
    IReadOnlyList<string> Errors)
{
    public bool AnyChanges => Added > 0 || Updated > 0;
    public int RowsProcessed => Added + Updated;
}
