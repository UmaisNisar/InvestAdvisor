using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Estimates AI spend from persisted run rows and enforces the daily budget. Backs both the
/// worker's budget guard and the cost-history UI so they report the same numbers.
/// </summary>
public interface ICostService
{
    /// <summary>Estimated USD spent on AI calls since UTC midnight today (all sources).</summary>
    Task<decimal> TodaySpendUsdAsync(CancellationToken ct = default);

    /// <summary>True when today's spend has reached the configured daily budget (0 = unlimited).</summary>
    Task<bool> IsOverDailyBudgetAsync(CancellationToken ct = default);

    /// <summary>Full breakdown over the trailing <paramref name="days"/> window.</summary>
    Task<CostReport> GetReportAsync(int days = 30, CancellationToken ct = default);
}
