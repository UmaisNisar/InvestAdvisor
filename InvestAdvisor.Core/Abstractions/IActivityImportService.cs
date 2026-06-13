namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Imports a Wealthsimple <b>Activity</b> (transactions) export and records each sell as a realized
/// P&amp;L lot. Unlike the holdings importer (which only models current positions), this captures the
/// permanent history of closed positions. Columns are matched flexibly by header name; rows are
/// de-duplicated by a stable per-row hash so re-uploading the same (or an overlapping) export never
/// double-counts. No credentials involved.
/// </summary>
public interface IActivityImportService
{
    Task<ActivityImportResult> ImportActivityCsvAsync(int tenantId, string csvContent, CancellationToken ct = default);
}

public sealed record ActivityImportResult(
    int Recorded,
    int Skipped,
    int Duplicates,
    IReadOnlyList<string> Errors)
{
    public bool AnyChanges => Recorded > 0;
}
