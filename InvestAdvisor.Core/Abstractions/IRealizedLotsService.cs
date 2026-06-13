using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Write operations for the realized-gains ledger. Reads go through
/// <see cref="IPortfolioQueries.GetRealizedLotsAsync"/>; bulk creation goes through
/// <see cref="IActivityImportService"/>. This is for hand corrections/additions.
/// </summary>
public interface IRealizedLotsService
{
    Task<RealizedLot> CreateAsync(RealizedLot input, CancellationToken ct = default);
    Task<RealizedLot> UpdateAsync(int id, RealizedLot input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
