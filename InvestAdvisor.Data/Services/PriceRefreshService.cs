using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

public sealed class PriceRefreshService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IMarketDataProvider provider,
    IRuntimeSettingsStore settingsStore,
    ISystemClock clock,
    ILogger<PriceRefreshService>? logger = null) : IPriceRefreshService
{
    public async Task<int> RefreshAsync(IEnumerable<TickerSpec> tickers, CancellationToken ct = default)
    {
        var specs = tickers.Distinct().ToArray();
        if (specs.Length == 0) return 0;

        var settings = await settingsStore.GetAsync(ct);
        var freshnessCutoff = clock.UtcNow - TimeSpan.FromSeconds(settings.MinPriceFreshnessSeconds);

        var tickerStrings = specs.Select(s => s.Ticker).ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var freshExisting = await db.PriceSnapshots.AsNoTracking()
            .Where(s => tickerStrings.Contains(s.Ticker) && s.FetchedAtUtc >= freshnessCutoff)
            .GroupBy(s => s.Ticker)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var freshSet = freshExisting.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var written = 0;
        foreach (var spec in specs)
        {
            if (freshSet.Contains(spec.Ticker)) continue;
            try
            {
                var quote = await provider.GetQuoteAsync(spec.Ticker, spec.AssetClass, ct);
                if (quote is null) continue;

                db.PriceSnapshots.Add(new PriceSnapshot
                {
                    Ticker = spec.Ticker.ToUpperInvariant(),
                    AssetClass = spec.AssetClass,
                    Price = quote.Price,
                    PreviousClose = quote.PreviousClose,
                    PercentChange = quote.PercentChange,
                    Currency = "USD",
                    FetchedAtUtc = quote.FetchedAtUtc,
                });
                written++;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Price refresh failed for {Ticker} ({AssetClass}); continuing.",
                    spec.Ticker, spec.AssetClass);
            }
        }

        if (written > 0) await db.SaveChangesAsync(ct);
        return written;
    }
}
