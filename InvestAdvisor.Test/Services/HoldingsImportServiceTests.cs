using System.Net.Http;
using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class HoldingsImportServiceTests
{
    private const int Tenant = 1;

    // A trimmed Wealthsimple holdings-report export (real column layout + trailing "As of" footer).
    private const string WsCsv = """
        Account Name,Account Type,Account Classification,Account Number,Symbol,Exchange,MIC,Name,Security Type,Quantity,Position Direction,Market Price,Market Price Currency,Book Value (CAD),Book Value Currency (CAD),Book Value (Market),Book Value Currency (Market),Market Value,Market Value Currency,Market Unrealized Returns,Market Unrealized Returns Currency
        "Crypto","Crypto","Trade","HQ1","BTC","","","Bitcoin","CRYPTOCURRENCY","0.01553933","LONG","87258.25","CAD","2000","CAD","2000","CAD","1356.25","CAD","-643.74","CAD"
        "TFSA","TFSA","Trade","HQ2","ELMT","NASDAQ","XNAS","Elmet Group","EQUITY","20","LONG","17.49","USD","553.5","CAD","390.60","USD","349.8","USD","-40.8","USD"
        "TFSA","TFSA","Trade","HQ2","IDIV.B","TSX","XTSE","Manulife Intl Dividend ETF","EXCHANGE_TRADED_FUND","36","LONG","20","CAD","727.72","CAD","727.72","CAD","720","CAD","-7.72","CAD"
        "TFSA","TFSA","Trade","HQ2","TOI","TSX-V","XTSX","Topicus","EQUITY","4","LONG","100","CAD","400","CAD","400","CAD","397","CAD","-3","CAD"

        "As of 2026-06-09 14:49 GMT+05:00"
        """;

    private static HoldingsImportService Build(SqliteFixture db)
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetRateToUsdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(ci => Task.FromResult(ci.Arg<string>().Equals("CAD", StringComparison.OrdinalIgnoreCase) ? 0.7175m : 1m));
        return new HoldingsImportService(db.Factory, httpFactory, fx);
    }

    [Fact]
    public async Task Wealthsimple_csv_imports_ticker_currency_and_cost_per_asset_class()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        var result = await sut.ImportCsvAsync(Tenant, WsCsv);

        result.Added.Should().Be(4);          // 4 holdings; the "As of …" footer is skipped
        result.Errors.Should().BeEmpty();

        using var c = db.CreateContext();
        var h = await c.Holdings.ToDictionaryAsync(x => x.Ticker);

        // Crypto → USD-forced (CoinGecko feed is USD); cost converted from the CAD book value.
        h.Should().ContainKey("BTC");
        h["BTC"].AssetClass.Should().Be(AssetClass.Crypto);
        h["BTC"].Currency.Should().Be("USD");
        h["BTC"].AvgCost.Should().BeApproximately(2000m * 0.7175m / 0.01553933m, 1m);

        // US equity → bare ticker, USD, cost = book / qty (same currency, no conversion).
        h.Should().ContainKey("ELMT");
        h["ELMT"].AssetClass.Should().Be(AssetClass.Equity);
        h["ELMT"].Currency.Should().Be("USD");
        h["ELMT"].AvgCost.Should().BeApproximately(390.60m / 20m, 0.01m);

        // Canadian ETF → Symbol + TSX → quotable IDIV.B.TO; CAD; cost in CAD.
        h.Should().ContainKey("IDIV.B.TO");
        h["IDIV.B.TO"].AssetClass.Should().Be(AssetClass.Etf);
        h["IDIV.B.TO"].Currency.Should().Be("CAD");
        h["IDIV.B.TO"].AvgCost.Should().BeApproximately(727.72m / 36m, 0.01m);

        // TSX Venture → .V suffix.
        h.Should().ContainKey("TOI.V");
        h["TOI.V"].Currency.Should().Be("CAD");

        // The footer line must not become a holding.
        h.Keys.Should().NotContain(k => k.StartsWith("AS OF"));
    }

    [Fact]
    public async Task Reimport_updates_in_place_keyed_on_ticker_and_account_no_duplicates()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        await sut.ImportCsvAsync(Tenant, WsCsv);
        var second = await sut.ImportCsvAsync(Tenant, WsCsv);

        second.Added.Should().Be(0);
        second.Updated.Should().Be(4);
        using var c = db.CreateContext();
        (await c.Holdings.CountAsync()).Should().Be(4);
    }
}
