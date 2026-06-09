using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// Seeds the screener universes the first time the app runs — one curated list per asset class
/// (equities, ETFs, crypto). Each class is seeded independently and only when empty, so you can
/// add/remove names from the UI without the seed reappearing.
/// </summary>
public sealed class StockUniverseSeeder(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ILogger<StockUniverseSeeder>? logger = null) : IStockUniverseSeeder
{
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var added = 0;

        added += await SeedClassAsync(db, AssetClass.Equity,
            Equities.Select(x => new Stock { Ticker = x.Ticker, Name = x.Name, Sector = x.Sector, AssetClass = AssetClass.Equity, IsActive = true, AddedAtUtc = now }), ct);

        added += await SeedClassAsync(db, AssetClass.Etf,
            Etfs.Select(x => new Stock { Ticker = x.Ticker, Name = x.Name, Sector = x.Focus, AssetClass = AssetClass.Etf, IsActive = true, AddedAtUtc = now }), ct);

        added += await SeedClassAsync(db, AssetClass.Crypto,
            Cryptos.Select(x => new Stock { Ticker = x.Symbol, Name = x.Name, Sector = "Crypto", AssetClass = AssetClass.Crypto, ExternalId = x.CoinGeckoId, IsActive = true, AddedAtUtc = now }), ct);

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<int> SeedClassAsync(InvestAdvisorDbContext db, AssetClass cls, IEnumerable<Stock> rows, CancellationToken ct)
    {
        if (await db.Stocks.AnyAsync(s => s.AssetClass == cls, ct)) return 0;
        var list = rows.ToList();
        db.Stocks.AddRange(list);
        logger?.LogInformation("Seeded {Count} {Class} into the screener universe.", list.Count, cls);
        return list.Count;
    }

    private static readonly (string Ticker, string Name, string Sector)[] Equities =
    [
        ("AAPL", "Apple Inc.", "Technology"), ("MSFT", "Microsoft Corp.", "Technology"),
        ("NVDA", "NVIDIA Corp.", "Technology"), ("GOOGL", "Alphabet Inc.", "Technology"),
        ("AMZN", "Amazon.com Inc.", "Consumer"), ("META", "Meta Platforms Inc.", "Technology"),
        ("AVGO", "Broadcom Inc.", "Technology"), ("ORCL", "Oracle Corp.", "Technology"),
        ("ADBE", "Adobe Inc.", "Technology"), ("CRM", "Salesforce Inc.", "Technology"),
        ("AMD", "Advanced Micro Devices", "Technology"), ("INTC", "Intel Corp.", "Technology"),
        ("CSCO", "Cisco Systems Inc.", "Technology"), ("QCOM", "Qualcomm Inc.", "Technology"),
        ("TXN", "Texas Instruments Inc.", "Technology"), ("IBM", "IBM Corp.", "Technology"),
        ("NOW", "ServiceNow Inc.", "Technology"), ("INTU", "Intuit Inc.", "Technology"),
        ("TSLA", "Tesla Inc.", "Consumer"), ("HD", "Home Depot Inc.", "Consumer"),
        ("NKE", "Nike Inc.", "Consumer"), ("MCD", "McDonald's Corp.", "Consumer"),
        ("SBUX", "Starbucks Corp.", "Consumer"), ("COST", "Costco Wholesale Corp.", "Consumer"),
        ("WMT", "Walmart Inc.", "Consumer"), ("TGT", "Target Corp.", "Consumer"),
        ("DIS", "Walt Disney Co.", "Communication"), ("NFLX", "Netflix Inc.", "Communication"),
        ("JPM", "JPMorgan Chase & Co.", "Financials"), ("BAC", "Bank of America Corp.", "Financials"),
        ("WFC", "Wells Fargo & Co.", "Financials"), ("GS", "Goldman Sachs Group", "Financials"),
        ("MS", "Morgan Stanley", "Financials"), ("V", "Visa Inc.", "Financials"),
        ("MA", "Mastercard Inc.", "Financials"), ("AXP", "American Express Co.", "Financials"),
        ("BRK.B", "Berkshire Hathaway Inc.", "Financials"), ("UNH", "UnitedHealth Group", "Healthcare"),
        ("JNJ", "Johnson & Johnson", "Healthcare"), ("LLY", "Eli Lilly and Co.", "Healthcare"),
        ("PFE", "Pfizer Inc.", "Healthcare"), ("MRK", "Merck & Co.", "Healthcare"),
        ("ABBV", "AbbVie Inc.", "Healthcare"), ("TMO", "Thermo Fisher Scientific", "Healthcare"),
        ("ABT", "Abbott Laboratories", "Healthcare"), ("XOM", "Exxon Mobil Corp.", "Energy"),
        ("CVX", "Chevron Corp.", "Energy"), ("CAT", "Caterpillar Inc.", "Industrials"),
        ("BA", "Boeing Co.", "Industrials"), ("GE", "GE Aerospace", "Industrials"),
        ("HON", "Honeywell International", "Industrials"), ("UPS", "United Parcel Service", "Industrials"),
        ("RTX", "RTX Corp.", "Industrials"), ("T", "AT&T Inc.", "Communication"),
        ("VZ", "Verizon Communications", "Communication"), ("CMCSA", "Comcast Corp.", "Communication"),
        ("KO", "Coca-Cola Co.", "Staples"), ("PEP", "PepsiCo Inc.", "Staples"),
        ("PG", "Procter & Gamble Co.", "Staples"), ("NEE", "NextEra Energy Inc.", "Utilities"),
    ];

    private static readonly (string Ticker, string Name, string Focus)[] Etfs =
    [
        ("SPY", "SPDR S&P 500 ETF", "Broad market"),
        ("QQQ", "Invesco QQQ (Nasdaq-100)", "Broad market"),
        ("IWM", "iShares Russell 2000", "Small cap"),
        ("DIA", "SPDR Dow Jones Industrial", "Broad market"),
        ("VTI", "Vanguard Total Stock Market", "Broad market"),
        ("XLK", "Technology Select Sector SPDR", "Technology"),
        ("XLF", "Financial Select Sector SPDR", "Financials"),
        ("XLE", "Energy Select Sector SPDR", "Energy"),
        ("XLV", "Health Care Select Sector SPDR", "Healthcare"),
        ("XLY", "Consumer Discretionary SPDR", "Consumer"),
        ("XLP", "Consumer Staples SPDR", "Staples"),
        ("XLI", "Industrial Select Sector SPDR", "Industrials"),
        ("XLU", "Utilities Select Sector SPDR", "Utilities"),
        ("XLB", "Materials Select Sector SPDR", "Materials"),
        ("XLRE", "Real Estate Select Sector SPDR", "Real estate"),
        ("XLC", "Communication Services SPDR", "Communication"),
        ("SMH", "VanEck Semiconductor ETF", "Semiconductors"),
        ("ARKK", "ARK Innovation ETF", "Innovation"),
        ("GLD", "SPDR Gold Shares", "Gold"),
        ("TLT", "iShares 20+ Year Treasury", "Bonds"),
        ("VNQ", "Vanguard Real Estate ETF", "Real estate"),
        ("EEM", "iShares MSCI Emerging Markets", "Emerging markets"),
    ];

    private static readonly (string Symbol, string Name, string CoinGeckoId)[] Cryptos =
    [
        ("BTC", "Bitcoin", "bitcoin"),
        ("ETH", "Ethereum", "ethereum"),
        ("BNB", "BNB", "binancecoin"),
        ("SOL", "Solana", "solana"),
        ("XRP", "XRP", "ripple"),
        ("ADA", "Cardano", "cardano"),
        ("DOGE", "Dogecoin", "dogecoin"),
        ("AVAX", "Avalanche", "avalanche-2"),
        ("DOT", "Polkadot", "polkadot"),
        ("LINK", "Chainlink", "chainlink"),
        ("MATIC", "Polygon", "matic-network"),
        ("LTC", "Litecoin", "litecoin"),
    ];
}
