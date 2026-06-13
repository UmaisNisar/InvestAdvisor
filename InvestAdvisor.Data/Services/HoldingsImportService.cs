using System.Net.Http;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static InvestAdvisor.Data.Services.CsvImportHelpers;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Parses a holdings CSV (Wealthsimple export or a hand-made file) and upserts the rows into the
/// portfolio. Headers are matched flexibly so it tolerates different export layouts. It reconstructs
/// quotable tickers (Symbol + exchange suffix, e.g. IDIV.B + TSX → IDIV.B.TO), reads the per-row
/// currency, and converts crypto cost to USD (our crypto feed is USD-priced). Upsert keys on
/// (ticker, account). By default positions not present in the file are left untouched; with
/// <c>replaceExisting</c> the file is the full portfolio and absent positions are removed.
/// </summary>
public sealed class HoldingsImportService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    IFxRateProvider fx,
    ILogger<HoldingsImportService>? logger = null) : IHoldingsImportService
{
    public async Task<HoldingsImportResult> ImportFromUrlAsync(int tenantId, string url, bool replaceExisting = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new HoldingsImportResult(0, 0, 0, new[] { "No URL configured." });

        string content;
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            content = await client.GetStringAsync(url, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new HoldingsImportResult(0, 0, 0, new[] { $"Couldn't fetch the CSV URL: {ex.Message}" });
        }

        return await ImportCsvAsync(tenantId, content, replaceExisting, ct);
    }

    public async Task<HoldingsImportResult> ImportCsvAsync(int tenantId, string csvContent, bool replaceExisting = false, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(csvContent))
            return new HoldingsImportResult(0, 0, 0, new[] { "The file was empty." });

        var rows = ParseCsv(csvContent);
        if (rows.Count < 2)
            return new HoldingsImportResult(0, 0, 0, new[] { "No data rows found (need a header row plus at least one holding)." });

        var header = rows[0];

        int symbolIdx = Find(header, h => h is "symbol" or "ticker" or "security" or "instrument");
        int qtyIdx = Find(header, h => h is "quantity" or "qty" or "shares" or "units");
        if (symbolIdx < 0 || qtyIdx < 0)
            return new HoldingsImportResult(0, 0, 0, new[]
            {
                "Couldn't find the required Symbol and Quantity columns. " +
                $"Found: {string.Join(", ", header)}",
            });

        int exchangeIdx = Find(header, h => h == "exchange");
        int micIdx = Find(header, h => h == "mic");
        int nameIdx = Find(header, h => h == "name" || h is "description" or "security name" or "company");
        int secTypeIdx = Find(header, h => h.Contains("security type") || h == "asset class" || h == "class");
        int acctIdx = Find(header, h => h.Contains("account type")); // not "Account Name"
        if (acctIdx < 0) acctIdx = Find(header, h => h.Contains("account") && h.Contains("type"));
        int priceCurIdx = Find(header, h => h.Contains("market price currency"));
        // Cost basis: prefer the market-currency book value; fall back to CAD book value / per-share cost.
        int bookMktIdx = Find(header, h => h.Contains("book value") && h.Contains("market") && !h.Contains("currency"));
        int bookMktCurIdx = Find(header, h => h.Contains("book value currency") && h.Contains("market"));
        int bookCadIdx = Find(header, h => h.Contains("book value") && h.Contains("cad") && !h.Contains("currency"));
        int bookAnyIdx = Find(header, h => h is "book cost" or "total cost" or "cost basis" or "book value");
        int avgIdx = Find(header, h => h is "avg cost" or "average cost" or "unit cost" or "cost per share");

        // Header/index matching, parsing and coercion live in the shared helper (see CsvImportHelpers).

        int added = 0, updated = 0, skipped = 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Holdings.Where(h => h.TenantId == tenantId).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var touched = new HashSet<Holding>(); // pre-existing rows the file mentioned (replace mode keeps these)

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            try
            {
                var symbol = Get(row, symbolIdx).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) { skipped++; continue; } // header gaps / "As of …" footer
                if (!TryDecimal(Get(row, qtyIdx), out var qty) || qty <= 0m) { skipped++; continue; }

                var assetClass = secTypeIdx >= 0 ? MapAssetClass(Get(row, secTypeIdx)) : AssetClass.Equity;
                var account = acctIdx >= 0 ? MapAccount(Get(row, acctIdx)) : AccountType.Taxable;
                var name = nameIdx >= 0 ? Get(row, nameIdx).Trim() : string.Empty;

                // Quotable ticker: append the exchange suffix our price providers expect.
                var ticker = ApplyExchangeSuffix(symbol, Get(row, exchangeIdx), Get(row, micIdx), assetClass);

                // Holding currency = the currency our price feed reports for it. Crypto → USD (CoinGecko),
                // otherwise the CSV's market-price currency. Stored UPPER-case to match the rest of the app
                // (the edit dialog's USD/CAD dropdown, the FX lookup, etc.).
                var priceCur = (assetClass == AssetClass.Crypto ? "USD" : Get(row, priceCurIdx)).Trim().ToUpperInvariant();
                if (priceCur.Length != 3) priceCur = "USD";

                // Average cost, expressed in the holding's currency.
                var avgCost = await ComputeAvgCostAsync(row, qty, priceCur, avgIdx, bookMktIdx, bookMktCurIdx, bookCadIdx, bookAnyIdx, ct);

                var match = existing.FirstOrDefault(h =>
                    h.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase) && h.AccountType == account);

                if (match is null)
                {
                    db.Holdings.Add(new Holding
                    {
                        TenantId = tenantId,
                        Ticker = ticker,
                        Name = name,
                        AssetClass = assetClass,
                        Quantity = qty,
                        AvgCost = avgCost,
                        Currency = priceCur,
                        AccountType = account,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    });
                    added++;
                }
                else
                {
                    match.Quantity = qty;
                    if (avgCost > 0m) match.AvgCost = avgCost;
                    match.Currency = priceCur;
                    match.AssetClass = assetClass;
                    if (!string.IsNullOrWhiteSpace(name)) match.Name = name;
                    match.UpdatedAtUtc = now;
                    touched.Add(match);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Row {r + 1}: {ex.Message}");
            }
        }

        // Replace mode: the file is the whole portfolio, so drop positions it doesn't mention.
        // Only when at least one row imported — a wrong/empty/garbled file must never wipe holdings.
        var removed = 0;
        if (replaceExisting && added + updated > 0)
        {
            var stale = existing.Where(h => !touched.Contains(h)).ToList();
            db.Holdings.RemoveRange(stale);
            removed = stale.Count;
        }

        await db.SaveChangesAsync(ct);
        logger?.LogInformation(
            "Holdings CSV import: {Added} added, {Updated} updated, {Removed} removed, {Skipped} skipped.",
            added, updated, removed, skipped);
        return new HoldingsImportResult(added, updated, skipped, errors, removed);
    }

    private async Task<decimal> ComputeAvgCostAsync(
        string[] row, decimal qty, string holdingCur,
        int avgIdx, int bookMktIdx, int bookMktCurIdx, int bookCadIdx, int bookAnyIdx, CancellationToken ct)
    {
        // A direct per-share cost wins if present.
        if (avgIdx >= 0 && TryDecimal(Get(row, avgIdx), out var perShare) && perShare > 0m)
            return perShare;

        // Otherwise derive from a book value (total cost), converting its currency to the holding's.
        decimal book = 0m;
        string bookCur = holdingCur;
        if (bookMktIdx >= 0 && TryDecimal(Get(row, bookMktIdx), out var bm) && bm > 0m)
        {
            book = bm;
            bookCur = bookMktCurIdx >= 0 ? Norm(Get(row, bookMktCurIdx)) : holdingCur;
        }
        else if (bookCadIdx >= 0 && TryDecimal(Get(row, bookCadIdx), out var bc) && bc > 0m)
        {
            book = bc;
            bookCur = "CAD";
        }
        else if (bookAnyIdx >= 0 && TryDecimal(Get(row, bookAnyIdx), out var ba) && ba > 0m)
        {
            book = ba;
        }

        if (book <= 0m || qty <= 0m) return 0m;
        if (bookCur.Length != 3) bookCur = holdingCur;
        if (!bookCur.Equals(holdingCur, StringComparison.OrdinalIgnoreCase))
            book = await ConvertAsync(book, bookCur, holdingCur, ct);
        return book / qty;
    }

    private async Task<decimal> ConvertAsync(decimal amount, string from, string to, CancellationToken ct)
    {
        var rFrom = await fx.GetRateToUsdAsync(from, ct);
        var rTo = await fx.GetRateToUsdAsync(to, ct);
        return rTo == 0m ? amount : amount * rFrom / rTo;
    }
}
