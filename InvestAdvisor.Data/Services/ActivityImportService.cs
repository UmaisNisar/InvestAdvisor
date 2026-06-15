using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static InvestAdvisor.Data.Services.CsvImportHelpers;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Parses a Wealthsimple Activity (transactions) export and records each <b>sell</b> as a
/// <see cref="RealizedLot"/>. Headers are matched flexibly. Cost basis is taken from a per-trade
/// return column when present (exact, matches Wealthsimple), otherwise reconstructed from a running
/// average of the file's buy rows, otherwise from the current holding's average cost, otherwise left
/// equal to proceeds (0 P&amp;L, user-editable). Rows are de-duplicated by a stable hash so re-uploading
/// the same or overlapping export never double-counts.
/// </summary>
public sealed class ActivityImportService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IFxRateProvider fx,
    ILogger<ActivityImportService>? logger = null) : IActivityImportService
{
    public async Task<ActivityImportResult> ImportActivityCsvAsync(int tenantId, string csvContent, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(csvContent))
            return new ActivityImportResult(0, 0, 0, new[] { "The file was empty." });

        var rows = ParseCsv(csvContent);
        if (rows.Count < 2)
            return new ActivityImportResult(0, 0, 0, new[] { "No data rows found (need a header row plus at least one activity)." });

        // Header layout (Wealthsimple Activity export), matched flexibly so other layouts still work:
        //   transaction_date, settlement_date, account_id, account_type, activity_type,
        //   activity_sub_type, direction, symbol, name, currency, quantity, unit_price,
        //   commission, net_cash_amount
        var header = rows[0];
        int dateIdx = Find(header, h => h is "transaction_date" or "trade date" or "date" || (h.Contains("date") && !h.Contains("settlement")));
        // Buy/Sell lives in activity_sub_type; fall back to a single transaction/type column.
        int typeIdx = Find(header, h => h is "activity_sub_type" or "transaction" or "type" or "action" || h.Contains("sub_type") || h.Contains("sub type"));
        int symbolIdx = Find(header, h => h is "symbol" or "ticker" or "security" or "instrument");
        int nameIdx = Find(header, h => h is "name" or "description" or "security name" or "company");
        int qtyIdx = Find(header, h => h.Contains("quantity") || h is "qty" or "shares" or "units");
        int priceIdx = Find(header, h => h.Contains("unit_price") || h is "unit price" or "price" or "fill price" || (h.Contains("price") && !h.Contains("currency")));
        // net_cash_amount is the cash that moved — negative on buys, positive on sells, already net of fees.
        int amountIdx = Find(header, h => h.Contains("net_cash_amount") || h.Contains("net cash") || h.Contains("amount") || h is "total value" or "value" or "total" or "proceeds");
        int curIdx = Find(header, h => h == "currency" || (h.Contains("currency") && !h.Contains("price")));
        int retIdx = Find(header, h => h is "return" or "gain" or "realized" or "p&l" or "pnl" or "profit" || h.Contains("realized") || h.Contains("return"));
        int exchangeIdx = Find(header, h => h == "exchange");
        int micIdx = Find(header, h => h == "mic");
        int secTypeIdx = Find(header, h => h.Contains("security type") || h == "asset class" || h == "class");
        // Prefer account_type over account_id (both contain "account").
        int acctIdx = Find(header, h => h.Contains("account") && h.Contains("type"));
        if (acctIdx < 0) acctIdx = Find(header, h => h is "account_type" or "account");

        if (symbolIdx < 0 || qtyIdx < 0 || amountIdx < 0 || typeIdx < 0)
            return new ActivityImportResult(0, 0, 0, new[]
            {
                "Couldn't find the required Symbol, Quantity, Amount and Buy/Sell columns. " +
                $"Found: {string.Join(", ", header)}",
            });

        // Parse every row into a transaction so buys can seed a running average for cost basis.
        var txns = new List<Txn>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var symbol = Get(row, symbolIdx).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol)) continue;
            var type = Get(row, typeIdx).Trim().ToLowerInvariant();
            var isSell = type.Contains("sell");
            var isBuy = type.Contains("buy");
            if (!isSell && !isBuy) continue; // ignore dividends, deposits, transfers, etc.

            // Wealthsimple signs quantity by direction (sells are negative); magnitude is what we record.
            if (!TryDecimal(Get(row, qtyIdx), out var qty)) continue;
            qty = Math.Abs(qty);
            if (qty == 0m) continue;
            TryDecimal(Get(row, priceIdx), out var price);
            if (!TryDecimal(Get(row, amountIdx), out var amount)) amount = 0m;
            amount = Math.Abs(amount);

            var assetClass = secTypeIdx >= 0 ? MapAssetClass(Get(row, secTypeIdx)) : AssetClass.Equity;
            var ticker = ApplyExchangeSuffix(symbol, Get(row, exchangeIdx), Get(row, micIdx), assetClass);
            var account = acctIdx >= 0 ? MapAccount(Get(row, acctIdx)) : AccountType.Taxable;
            var currency = NormCur(Get(row, curIdx));
            var name = nameIdx >= 0 ? Get(row, nameIdx).Trim() : string.Empty;
            var date = ParseDate(Get(row, dateIdx));
            decimal? ret = retIdx >= 0 && TryDecimal(Get(row, retIdx), out var rv) ? rv : null;

            txns.Add(new Txn(r + 1, date, isSell, ticker, name, assetClass, account, qty, price, amount, currency, ret));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existingHashes = (await db.RealizedLots.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.SourceHash != "")
                .Select(l => l.SourceHash).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var holdingCost = await db.Holdings.AsNoTracking()
            .Where(h => h.TenantId == tenantId)
            .ToDictionaryAsync(h => (h.Ticker, h.AccountType), h => (h.AvgCost, h.Currency), ct);

        // Running average per position, replayed oldest-first so a sell's cost basis reflects prior buys.
        var running = new Dictionary<(string, AccountType), (decimal Qty, decimal Cost)>();
        int recorded = 0, skipped = 0, duplicates = 0;
        var batchHashes = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var t in txns.OrderBy(t => t.Date))
        {
            var key = (t.Ticker, t.Account);
            if (!t.IsSell)
            {
                // Prefer the actual cash paid (net_cash_amount, fees included); fall back to qty × price.
                var add = t.Amount > 0m ? t.Amount : t.Qty * t.Price;
                var cur = running.GetValueOrDefault(key);
                running[key] = (cur.Qty + t.Qty, cur.Cost + add);
                continue;
            }

            decimal costBasis = await CostBasisForSellAsync(t, running, holdingCost, ct);
            // Consume the sold shares from the running average so later sells stay correct.
            if (running.TryGetValue(key, out var st) && st.Qty > 0m)
            {
                var consumed = Math.Min(t.Qty, st.Qty);
                var avg = st.Cost / st.Qty;
                running[key] = (st.Qty - consumed, st.Cost - avg * consumed);
            }

            var hash = Hash(tenantId, t);
            if (!batchHashes.Add(hash) || existingHashes.Contains(hash)) { duplicates++; continue; }

            db.RealizedLots.Add(new RealizedLot
            {
                TenantId = tenantId,
                Ticker = t.Ticker,
                Name = t.Name,
                AssetClass = t.AssetClass,
                AccountType = t.Account,
                Quantity = t.Qty,
                Proceeds = t.Amount,
                CostBasis = costBasis,
                Currency = t.Currency,
                RealizedAtUtc = t.Date,
                SourceHash = hash,
                ManualEntry = false,
                CreatedAtUtc = now,
            });
            recorded++;
        }

        await db.SaveChangesAsync(ct);
        logger?.LogInformation(
            "Activity import: {Recorded} realized lots recorded, {Duplicates} duplicates, {Skipped} skipped.",
            recorded, duplicates, skipped);
        return new ActivityImportResult(recorded, skipped, duplicates, errors);
    }

    private async Task<decimal> CostBasisForSellAsync(
        Txn t,
        IReadOnlyDictionary<(string, AccountType), (decimal Qty, decimal Cost)> running,
        IReadOnlyDictionary<(string, AccountType), (decimal AvgCost, string Currency)> holdingCost,
        CancellationToken ct)
    {
        // (a) Exact: Wealthsimple's per-trade return → cost basis = proceeds − return.
        if (t.Return is { } ret) return t.Amount - ret;

        // (b) Running average reconstructed from this file's buys.
        if (running.TryGetValue((t.Ticker, t.Account), out var st) && st.Qty > 0m)
            return st.Cost / st.Qty * t.Qty;

        // (c) Fall back to the position's current average cost (convert if denominated differently).
        if (holdingCost.TryGetValue((t.Ticker, t.Account), out var hc) && hc.AvgCost > 0m)
        {
            var basis = hc.AvgCost * t.Qty;
            if (!NormCur(hc.Currency).Equals(t.Currency, StringComparison.OrdinalIgnoreCase))
                basis = await ConvertAsync(basis, hc.Currency, t.Currency, ct);
            return basis;
        }

        // (d) Unknown → 0 P&L, flagged for the user to edit.
        return t.Amount;
    }

    private async Task<decimal> ConvertAsync(decimal amount, string from, string to, CancellationToken ct)
    {
        var rFrom = await fx.GetRateToUsdAsync(NormCur(from), ct);
        var rTo = await fx.GetRateToUsdAsync(NormCur(to), ct);
        return rTo == 0m ? amount : amount * rFrom / rTo;
    }

    private static string NormCur(string? c)
    {
        c = (c ?? "").Trim().ToUpperInvariant();
        return c.Length == 3 ? c : "USD";
    }

    private static DateTime ParseDate(string s)
    {
        s = s.Trim();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return DateTime.UtcNow;
    }

    private static string Hash(int tenantId, Txn t)
    {
        var raw = string.Create(CultureInfo.InvariantCulture,
            $"{tenantId}|{t.Date:yyyy-MM-dd}|{t.Ticker}|{t.Account}|{t.Qty}|{t.Amount}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes, 0, 16); // 32 hex chars, well within the 64-char column
    }

    private sealed record Txn(
        int RowNum, DateTime Date, bool IsSell, string Ticker, string Name, AssetClass AssetClass,
        AccountType Account, decimal Qty, decimal Price, decimal Amount, string Currency, decimal? Return);
}
