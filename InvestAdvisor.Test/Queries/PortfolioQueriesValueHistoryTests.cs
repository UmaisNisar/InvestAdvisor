using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Data.Queries;
using InvestAdvisor.Test.TestHelpers;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Queries;

public class PortfolioQueriesValueHistoryTests
{
    private const int Tenant = 1;

    private static readonly DateTime D1 = new(2026, 6, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = new(2026, 6, 9, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime D3 = new(2026, 6, 10, 13, 30, 0, DateTimeKind.Utc);

    private static PortfolioQueries Build(SqliteFixture db, IPriceHistoryProvider history)
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetRateToUsdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(ci => Task.FromResult(ci.Arg<string>().Equals("CAD", StringComparison.OrdinalIgnoreCase) ? 0.5m : 1m));
        var tenant = Substitute.For<ITenantContext>();
        tenant.GetTenantIdAsync(Arg.Any<CancellationToken>()).Returns(Tenant);
        return new PortfolioQueries(db.Factory, fx, history, tenant);
    }

    private static async Task SeedHoldingAsync(SqliteFixture db, string ticker, decimal qty, string currency)
    {
        await using var c = db.CreateContext();
        c.Holdings.Add(new Holding { TenantId = Tenant, Ticker = ticker, Name = ticker, AssetClass = AssetClass.Equity, Quantity = qty, AvgCost = 1m, Currency = currency });
        await c.SaveChangesAsync();
    }

    private static PriceHistory History(string ticker, string currency, params (DateTime T, decimal Close)[] bars) =>
        new(ticker, currency, bars.Select(b => new Candle(b.T, b.Close, b.Close, b.Close, b.Close, 0)).ToArray());

    [Fact]
    public async Task Merges_multi_currency_series_with_fx_and_aligns_daily_bars_by_date()
    {
        await using var db = new SqliteFixture();
        // 10 shares of a TSX name (CAD, rate 0.5) and 2 shares of a US name (USD).
        await SeedHoldingAsync(db, "MU.TO", 10m, "CAD");
        await SeedHoldingAsync(db, "ELMT", 2m, "USD");

        var history = Substitute.For<IPriceHistoryProvider>();
        // Toronto bars stamped at 13:30 UTC, New York at 14:30 — same sessions, different opens.
        history.GetHistoryAsync("MU.TO", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns(History("MU.TO", "CAD", (D1, 40m), (D2, 42m), (D3, 41m)));
        history.GetHistoryAsync("ELMT", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns(History("ELMT", "USD", (D1.AddHours(1), 18m), (D2.AddHours(1), 19m), (D3.AddHours(1), 20m)));

        var result = await Build(db, history).GetValueHistoryAsync(HistoryRange.OneMonth);

        result.MissingTickers.Should().BeEmpty();
        // Daily bars collapse to dates, so both exchanges land on one point per session.
        result.Points.Should().HaveCount(3);
        result.Points.Select(p => p.TimeUtc).Should().Equal(D1.Date, D2.Date, D3.Date);
        // Day 1: 10 × 40 CAD × 0.5 + 2 × 18 USD = 200 + 36.
        result.Points[0].ValueUsd.Should().Be(236m);
        result.Points[1].ValueUsd.Should().Be(248m);
        result.Points[2].ValueUsd.Should().Be(245m);
    }

    [Fact]
    public async Task Reports_tickers_without_history_and_charts_the_rest()
    {
        await using var db = new SqliteFixture();
        await SeedHoldingAsync(db, "IDIV.B.TO", 36m, "CAD");
        await SeedHoldingAsync(db, "GHOST.V", 5m, "CAD");

        var history = Substitute.For<IPriceHistoryProvider>();
        history.GetHistoryAsync("IDIV.B.TO", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns(History("IDIV.B.TO", "CAD", (D1, 20m), (D2, 21m)));
        history.GetHistoryAsync("GHOST.V", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns((PriceHistory?)null);

        var result = await Build(db, history).GetValueHistoryAsync(HistoryRange.OneMonth);

        result.MissingTickers.Should().Equal("GHOST.V");
        result.Points.Should().HaveCount(2);
        result.Points[0].ValueUsd.Should().Be(36m * 20m * 0.5m);
    }

    [Fact]
    public async Task Forward_fills_gaps_and_backfills_before_a_series_starts()
    {
        await using var db = new SqliteFixture();
        await SeedHoldingAsync(db, "TOI.V", 4m, "CAD");
        await SeedHoldingAsync(db, "NEW.TO", 10m, "CAD");

        var history = Substitute.For<IPriceHistoryProvider>();
        // TOI trades all three sessions; NEW only lists on day 2 (e.g. IPO/halt).
        history.GetHistoryAsync("TOI.V", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns(History("TOI.V", "CAD", (D1, 100m), (D2, 102m), (D3, 104m)));
        history.GetHistoryAsync("NEW.TO", AssetClass.Equity, HistoryRange.OneMonth, Arg.Any<CancellationToken>())
            .Returns(History("NEW.TO", "CAD", (D2, 10m), (D3, 11m)));

        var result = await Build(db, history).GetValueHistoryAsync(HistoryRange.OneMonth);

        result.Points.Should().HaveCount(3);
        // Day 1: NEW has no bar yet → its first close (10) is held flat so the line doesn't jump.
        result.Points[0].ValueUsd.Should().Be((4m * 100m + 10m * 10m) * 0.5m);
        result.Points[1].ValueUsd.Should().Be((4m * 102m + 10m * 10m) * 0.5m);
        result.Points[2].ValueUsd.Should().Be((4m * 104m + 10m * 11m) * 0.5m);
    }

    [Fact]
    public async Task Intraday_ranges_keep_bar_timestamps_and_sum_same_ticker_across_accounts()
    {
        await using var db = new SqliteFixture();
        // Same ticker held in two accounts → quantities sum, history fetched once.
        await SeedHoldingAsync(db, "VFV.TO", 3m, "CAD");
        await SeedHoldingAsync(db, "VFV.TO", 1.2465m, "CAD");

        var t1 = new DateTime(2026, 6, 10, 13, 35, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 6, 10, 13, 40, 0, DateTimeKind.Utc);
        var history = Substitute.For<IPriceHistoryProvider>();
        history.GetHistoryAsync("VFV.TO", AssetClass.Equity, HistoryRange.OneDay, Arg.Any<CancellationToken>())
            .Returns(History("VFV.TO", "CAD", (t1, 180m), (t2, 181m)));

        var result = await Build(db, history).GetValueHistoryAsync(HistoryRange.OneDay);

        await history.Received(1).GetHistoryAsync("VFV.TO", AssetClass.Equity, HistoryRange.OneDay, Arg.Any<CancellationToken>());
        result.Points.Select(p => p.TimeUtc).Should().Equal(t1, t2);
        result.Points[0].ValueUsd.Should().Be(4.2465m * 180m * 0.5m);
    }
}
