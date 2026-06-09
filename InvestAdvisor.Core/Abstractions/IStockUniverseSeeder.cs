namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Inserts the curated screener universe into the <c>Stocks</c> table on first run
/// (no-op once seeded). Returns the number of rows added.
/// </summary>
public interface IStockUniverseSeeder
{
    Task<int> SeedAsync(CancellationToken ct = default);
}
