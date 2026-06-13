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

        // Persist the class seeds before the swing pass, which reads existing rows back to flip their
        // swing flag (a name like AAPL belongs to both universes) and to add the TSX/mover names.
        if (added > 0) await db.SaveChangesAsync(ct);

        added += await SeedSwingAsync(db, now, ct);
        return added;
    }

    /// <summary>
    /// Marks/adds the short-horizon swing universe: liquid US movers and liquid TSX large-caps. Runs
    /// once (when no swing member exists yet), so the user can curate it from the UI afterwards.
    /// Existing universe members are flipped in place; new names (TSX, higher-beta movers) are added.
    /// </summary>
    private async Task<int> SeedSwingAsync(InvestAdvisorDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.Stocks.AnyAsync(s => s.IsSwingUniverse, ct)) return 0;

        var existing = await db.Stocks.ToListAsync(ct); // tracked, so flag flips persist
        var byTicker = existing.ToDictionary(s => s.Ticker, StringComparer.OrdinalIgnoreCase);

        var n = 0;
        foreach (var (ticker, name, sector) in SwingNames)
        {
            if (byTicker.TryGetValue(ticker, out var st))
            {
                if (!st.IsSwingUniverse) { st.IsSwingUniverse = true; n++; }
            }
            else
            {
                db.Stocks.Add(new Stock
                {
                    Ticker = ticker, Name = name, Sector = sector,
                    AssetClass = AssetClass.Equity, IsActive = true, IsSwingUniverse = true, AddedAtUtc = now,
                });
                n++;
            }
        }

        if (n > 0) { await db.SaveChangesAsync(ct); logger?.LogInformation("Seeded {Count} swing-universe members.", n); }
        return n;
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

    /// <summary>
    /// Swing universe: liquid names that move enough for a 2–3 day trade and have clean Yahoo bars.
    /// US large-caps + higher-beta movers, plus liquid TSX large-caps (.TO). Deliberately excludes
    /// thin TSX-V venture names — the liquidity gate would drop them anyway, but they don't belong
    /// here. Names already in the equity/ETF universe are flipped in place; the rest are added.
    /// </summary>
    private static readonly (string Ticker, string Name, string Sector)[] SwingNames =
    [
        // US large-caps & movers
        ("AAPL", "Apple Inc.", "Technology"), ("MSFT", "Microsoft Corp.", "Technology"),
        ("NVDA", "NVIDIA Corp.", "Technology"), ("AMD", "Advanced Micro Devices", "Technology"),
        ("TSLA", "Tesla Inc.", "Consumer"), ("META", "Meta Platforms Inc.", "Technology"),
        ("AMZN", "Amazon.com Inc.", "Consumer"), ("GOOGL", "Alphabet Inc.", "Technology"),
        ("NFLX", "Netflix Inc.", "Communication"), ("AVGO", "Broadcom Inc.", "Technology"),
        ("MU", "Micron Technology", "Technology"), ("QCOM", "Qualcomm Inc.", "Technology"),
        ("INTC", "Intel Corp.", "Technology"), ("CRM", "Salesforce Inc.", "Technology"),
        ("ORCL", "Oracle Corp.", "Technology"), ("PLTR", "Palantir Technologies", "Technology"),
        ("COIN", "Coinbase Global", "Financials"), ("SMCI", "Super Micro Computer", "Technology"),
        ("UBER", "Uber Technologies", "Technology"), ("BA", "Boeing Co.", "Industrials"),
        ("DIS", "Walt Disney Co.", "Communication"), ("JPM", "JPMorgan Chase & Co.", "Financials"),
        ("BAC", "Bank of America Corp.", "Financials"), ("XOM", "Exxon Mobil Corp.", "Energy"),
        ("CVX", "Chevron Corp.", "Energy"), ("LLY", "Eli Lilly and Co.", "Healthcare"),
        // Liquid swing ETFs
        ("SPY", "SPDR S&P 500 ETF", "Broad market"), ("QQQ", "Invesco QQQ", "Broad market"),
        ("IWM", "iShares Russell 2000", "Small cap"), ("SMH", "VanEck Semiconductor ETF", "Semiconductors"),
        // Liquid TSX large-caps (Yahoo .TO)
        ("RY.TO", "Royal Bank of Canada", "Financials"), ("TD.TO", "Toronto-Dominion Bank", "Financials"),
        ("BNS.TO", "Bank of Nova Scotia", "Financials"), ("BMO.TO", "Bank of Montreal", "Financials"),
        ("CM.TO", "CIBC", "Financials"), ("ENB.TO", "Enbridge Inc.", "Energy"),
        ("CNQ.TO", "Canadian Natural Resources", "Energy"), ("SU.TO", "Suncor Energy", "Energy"),
        ("TRP.TO", "TC Energy", "Energy"), ("CVE.TO", "Cenovus Energy", "Energy"),
        ("CNR.TO", "Canadian National Railway", "Industrials"), ("CP.TO", "Canadian Pacific Kansas City", "Industrials"),
        ("SHOP.TO", "Shopify Inc.", "Technology"), ("BCE.TO", "BCE Inc.", "Communication"),
        ("MFC.TO", "Manulife Financial", "Financials"), ("SLF.TO", "Sun Life Financial", "Financials"),
        ("ABX.TO", "Barrick Gold", "Materials"), ("AEM.TO", "Agnico Eagle Mines", "Materials"),
        ("NTR.TO", "Nutrien Ltd.", "Materials"), ("WCN.TO", "Waste Connections", "Industrials"),
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
