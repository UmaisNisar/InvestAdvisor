using System.Globalization;
using System.Text;
using InvestAdvisor.Core.Enums;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// CSV plumbing shared by the holdings importer and the Activity (transactions) importer:
/// quote-aware line parsing, flexible header matching, decimal/account/asset-class coercion, and
/// the exchange-suffix logic our price providers expect. Kept free of any DB/FX dependency so both
/// services can reuse it without coupling.
/// </summary>
internal static class CsvImportHelpers
{
    public static List<string[]> ParseCsv(string content)
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

    public static string[] ParseLine(string line)
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

    public static int Find(string[] header, Func<string, bool> pred)
    {
        for (var i = 0; i < header.Length; i++)
            if (pred(Norm(header[i]))) return i;
        return -1;
    }

    public static string Norm(string s) => s.Trim().ToLowerInvariant();

    public static string Get(string[] row, int idx) => idx >= 0 && idx < row.Length ? row[idx] : string.Empty;

    public static bool TryDecimal(string s, out decimal value)
    {
        s = s.Trim().Replace("$", "").Replace(",", "").Replace("%", "");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    public static AccountType MapAccount(string s) => s.Trim().ToLowerInvariant() switch
    {
        "roth" or "rothira" or "roth ira" or "tfsa" => AccountType.RothIra,
        "traditional" or "traditionalira" or "ira" or "rrsp" => AccountType.TraditionalIra,
        "401k" or "brokerage401k" => AccountType.Brokerage401k,
        "hsa" => AccountType.Hsa,
        "taxable" or "personal" or "non-registered" or "cash" or "crypto" or "" => AccountType.Taxable,
        _ => AccountType.Other,
    };

    public static AssetClass MapAssetClass(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (s.Contains("etf") || s.Contains("exchange_traded_fund") || s.Contains("exchange traded fund") || s.Contains("fund"))
            return AssetClass.Etf;
        if (s.Contains("crypto") || s.Contains("coin"))
            return AssetClass.Crypto;
        return AssetClass.Equity;
    }

    /// <summary>Appends the exchange suffix our quote providers expect (Yahoo for non-US). No-op for
    /// US listings and crypto, or when the symbol already carries a suffix.</summary>
    public static string ApplyExchangeSuffix(string symbol, string exchange, string mic, AssetClass assetClass)
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
}
