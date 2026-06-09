using System.Globalization;
using System.Net.Http;
using System.Text;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Parses a holdings CSV (Wealthsimple export or a hand-made file) and upserts the rows into the
/// portfolio. Headers are matched flexibly so it tolerates different export layouts. It reconstructs
/// quotable tickers (Symbol + exchange suffix, e.g. IDIV.B + TSX → IDIV.B.TO), reads the per-row
/// currency, and converts crypto cost to USD (our crypto feed is USD-priced). Upsert keys on
/// (ticker, account); positions not present in the file are left untouched (non-destructive).
/// </summary>
public sealed class HoldingsImportService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    IFxRateProvider fx,
    ILogger<HoldingsImportService>? logger = null) : IHoldingsImportService
{
    public async Task<HoldingsImportResult> ImportFromUrlAsync(int tenantId, string url, CancellationToken ct = default)
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

        return await ImportCsvAsync(tenantId, content, ct);
    }

    public async Task<HoldingsImportResult> ImportCsvAsync(int tenantId, string csvContent, CancellationToken ct = default)
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

        int added = 0, updated = 0, skipped = 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Holdings.Where(h => h.TenantId == tenantId).ToListAsync(ct);
        var now = DateTime.UtcNow;

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
                // otherwise the CSV's market-price currency.
                var priceCur = Norm(assetClass == AssetClass.Crypto ? "USD" : Get(row, priceCurIdx));
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
                    updated++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Row {r + 1}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("Holdings CSV import: {Added} added, {Updated} updated, {Skipped} skipped.", added, updated, skipped);
        return new HoldingsImportResult(added, updated, skipped, errors);
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

    /// <summary>Appends the exchange suffix our quote providers expect (Yahoo for non-US). No-op for
    /// US listings and crypto, or when the symbol already carries a suffix.</summary>
    private static string ApplyExchangeSuffix(string symbol, string exchange, string mic, AssetClass assetClass)
    {
        if (assetClass == AssetClass.Crypto) return symbol;
        var suffix = ExchangeSuffix(Norm(exchange), Norm(mic));
        if (suffix.Length == 0) return symbol;
        return symbol.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? symbol : symbol + suffix;
    }

    private static string ExchangeSuffix(string exchange, string mic) => (exchange, mic) switch
    {
        _ when mic is "xnas" or "xnys" or "arcx" or "bats" or "xase" => "",        // US
        _ when exchange is "nasdaq" or "nyse" or "nyse arca" or "arca" or "amex" or "bats" or "cboe" => "",
        _ when exchange is "tsx" or "toronto" || mic == "xtse" => ".TO",            // Toronto
        _ when exchange is "tsx-v" or "tsxv" or "tsx venture" || mic == "xtsx" => ".V", // TSX Venture
        _ when exchange is "neo" or "cboe canada" or "neo exchange" || mic is "neoe" => ".NE", // Cboe Canada
        _ when exchange == "cse" || mic == "xcnq" => ".CN",                          // Canadian Securities Exchange
        _ when exchange == "asx" || mic == "xasx" => ".AX",                          // Australia
        _ when exchange == "lse" || mic == "xlon" => ".L",                           // London
        _ => "",
    };

    private static int Find(string[] header, Func<string, bool> pred)
    {
        for (var i = 0; i < header.Length; i++)
            if (pred(Norm(header[i]))) return i;
        return -1;
    }

    private static string Norm(string s) => s.Trim().ToLowerInvariant();

    private static string Get(string[] row, int idx) => idx >= 0 && idx < row.Length ? row[idx] : string.Empty;

    private static bool TryDecimal(string s, out decimal value)
    {
        s = s.Trim().Replace("$", "").Replace(",", "").Replace("%", "");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static AccountType MapAccount(string s) => s.Trim().ToLowerInvariant() switch
    {
        "roth" or "rothira" or "roth ira" or "tfsa" => AccountType.RothIra,
        "traditional" or "traditionalira" or "ira" or "rrsp" => AccountType.TraditionalIra,
        "401k" or "brokerage401k" => AccountType.Brokerage401k,
        "hsa" => AccountType.Hsa,
        "taxable" or "personal" or "non-registered" or "cash" or "crypto" or "" => AccountType.Taxable,
        _ => AccountType.Other,
    };

    private static AssetClass MapAssetClass(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (s.Contains("etf") || s.Contains("exchange_traded_fund") || s.Contains("exchange traded fund") || s.Contains("fund"))
            return AssetClass.Etf;
        if (s.Contains("crypto") || s.Contains("coin"))
            return AssetClass.Crypto;
        return AssetClass.Equity;
    }

    private static List<string[]> ParseCsv(string content)
    {
        var rows = new List<string[]>();
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(ParseLine(line));
        }
        return rows;
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}
