using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class ActivityImportServiceTests
{
    private const int Tenant = 1;

    // Real Wealthsimple Activity export layout. The export has no per-trade "return" column, so cost
    // basis is reconstructed from the running average of BUY rows. These two buys (MU $500, ELMT
    // $553.50) are the user's actual rows; the two SELLs are added here because Wealthsimple's export
    // lags settlement and didn't yet include them.
    private const string ActivityCsv = """
        transaction_date,settlement_date,account_id,account_type,activity_type,activity_sub_type,direction,symbol,name,currency,quantity,unit_price,commission,net_cash_amount
        2026-06-08,2026-06-08,HQ,TFSA,Trade,BUY,LONG,MU,Micron CDR (CAD Hedged),CAD,12.0452,41.51,0,-500
        2026-06-08,2026-06-08,HQ,TFSA,Trade,BUY,LONG,ELMT,Elmet Group Company (The),CAD,20,27.67512321,0,-553.5
        2026-06-12,2026-06-12,HQ,TFSA,Trade,SELL,LONG,MU,Micron CDR (CAD Hedged),CAD,-12.0452,43.78,0,527.34
        2026-06-12,2026-06-12,HQ,TFSA,Trade,SELL,LONG,ELMT,Elmet Group Company (The),CAD,-20,28.799,0,575.98
        """;

    // The user's actual export to date: buys, EFT deposits and interest — but no sells yet.
    private const string BuysOnlyCsv = """
        transaction_date,settlement_date,account_id,account_type,activity_type,activity_sub_type,direction,symbol,name,currency,quantity,unit_price,commission,net_cash_amount
        2026-05-25,,HQ,TFSA,MoneyMovement,EFT,,,,CAD,3000,,,3000
        2026-06-08,2026-06-08,HQ,TFSA,Trade,BUY,LONG,MU,Micron CDR (CAD Hedged),CAD,12.0452,41.51,0,-500
        2026-06-08,2026-06-08,HQ,TFSA,Trade,BUY,LONG,ELMT,Elmet Group Company (The),CAD,20,27.67512321,0,-553.5
        2026-06-12,,HQ,TFSA,Interest,,,,,CAD,0.01,,,0.01
        """;

    private static ActivityImportService Build(SqliteFixture db)
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetRateToUsdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(ci => Task.FromResult(ci.Arg<string>().Equals("CAD", StringComparison.OrdinalIgnoreCase) ? 0.7175m : 1m));
        return new ActivityImportService(db.Factory, fx);
    }

    [Fact]
    public async Task Sells_record_realized_lots_with_cost_basis_from_prior_buys()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        var result = await sut.ImportActivityCsvAsync(Tenant, ActivityCsv);

        result.Recorded.Should().Be(2);
        result.Errors.Should().BeEmpty();

        using var c = db.CreateContext();
        var lots = await c.RealizedLots.ToDictionaryAsync(l => l.Ticker);

        lots.Should().ContainKey("MU");
        var mu = lots["MU"];
        mu.AccountType.Should().Be(AccountType.RothIra); // TFSA → RothIra
        mu.Quantity.Should().Be(12.0452m);
        mu.Proceeds.Should().Be(527.34m);
        mu.CostBasis.Should().BeApproximately(500.00m, 0.001m);   // from the BUY row
        (mu.Proceeds - mu.CostBasis).Should().BeApproximately(27.34m, 0.001m);
        mu.Currency.Should().Be("CAD");

        lots.Should().ContainKey("ELMT");
        var elmt = lots["ELMT"];
        elmt.Quantity.Should().Be(20m);
        elmt.Proceeds.Should().Be(575.98m);
        elmt.CostBasis.Should().BeApproximately(553.50m, 0.001m);
        (elmt.Proceeds - elmt.CostBasis).Should().BeApproximately(22.48m, 0.001m);
    }

    [Fact]
    public async Task Reimporting_the_same_export_does_not_duplicate()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        await sut.ImportActivityCsvAsync(Tenant, ActivityCsv);
        var second = await sut.ImportActivityCsvAsync(Tenant, ActivityCsv);

        second.Recorded.Should().Be(0);
        second.Duplicates.Should().Be(2);
        using var c = db.CreateContext();
        (await c.RealizedLots.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Buys_eft_and_interest_rows_record_nothing()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        var result = await sut.ImportActivityCsvAsync(Tenant, BuysOnlyCsv);

        result.Recorded.Should().Be(0); // no SELL rows → no realized lots
        using var c = db.CreateContext();
        (await c.RealizedLots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cost_basis_uses_running_average_across_multiple_buys()
    {
        await using var db = new SqliteFixture();
        var sut = Build(db);

        // Two buys (avg cost = $15/sh), then sell 10 for $250 → cost basis $150, realized $100.
        var csv = """
            transaction_date,settlement_date,account_id,account_type,activity_type,activity_sub_type,direction,symbol,name,currency,quantity,unit_price,commission,net_cash_amount
            2026-06-01,2026-06-01,HQ,Personal,Trade,BUY,LONG,FOO,Foo Corp,USD,10,10,0,-100
            2026-06-02,2026-06-02,HQ,Personal,Trade,BUY,LONG,FOO,Foo Corp,USD,10,20,0,-200
            2026-06-12,2026-06-12,HQ,Personal,Trade,SELL,LONG,FOO,Foo Corp,USD,-10,25,0,250
            """;
        var result = await sut.ImportActivityCsvAsync(Tenant, csv);

        result.Recorded.Should().Be(1);
        using var c = db.CreateContext();
        var lot = await c.RealizedLots.SingleAsync();
        lot.Proceeds.Should().Be(250m);
        lot.CostBasis.Should().BeApproximately(150m, 0.001m);
    }
}
