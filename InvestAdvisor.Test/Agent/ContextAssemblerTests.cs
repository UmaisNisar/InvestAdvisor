using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Data.Agent;
using InvestAdvisor.Test.TestHelpers;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class ContextAssemblerTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 14, 30, 0, DateTimeKind.Utc);
    private static readonly RunTrigger DummyTrigger = new(RunTriggerKind.Scheduled, "test");
    private const int TestTenant = 0; // matches the default TenantId on the seeded holdings/watchlist

    private static (ContextAssembler assembler, SqliteFixture db, FakeSystemClock clock)
        BuildSut(int minFreshnessSec = 3600, decimal cadToUsd = 1m)
    {
        var db = new SqliteFixture();
        var clock = new FakeSystemClock(Now);
        var store = Substitute.For<IRuntimeSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>())
             .Returns(new ValueTask<RuntimeSettings>(new RuntimeSettings
             {
                 Id = RuntimeSettings.SingletonId,
                 MinPriceFreshnessSeconds = minFreshnessSec,
             }));
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetRateToUsdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(ci => Task.FromResult(ci.Arg<string>().Equals("CAD", StringComparison.OrdinalIgnoreCase) ? cadToUsd : 1m));

        // The singleton-profile seed is gone post multi-tenancy; seed the test tenant's profile
        // (TenantId 0, matching the default on the holdings/watchlist these tests add).
        using (var c = db.CreateContext())
        {
            c.Profiles.Add(new Profile { TenantId = TestTenant, GoalsText = "test goals", UpdatedAtUtc = Now });
            c.SaveChanges();
        }

        var sentiment = Substitute.For<ISentimentScoringService>();
        sentiment.GetTickerSentimentAsync(Arg.Any<CancellationToken>())
                 .Returns(new Dictionary<string, TickerSentiment>(StringComparer.OrdinalIgnoreCase));

        var assembler = new ContextAssembler(db.Factory, store, clock, fx, sentiment);
        return (assembler, db, clock);
    }

    [Fact]
    public async Task Empty_portfolio_returns_zeroed_totals_and_no_holdings()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        ctx.Holdings.Should().BeEmpty();
        ctx.Totals.MarketValueUsd.Should().Be(0m);
        ctx.Totals.UnrealizedPnlUsd.Should().Be(0m);
        ctx.Totals.UnrealizedPnlPct.Should().Be(0m);
        ctx.Totals.TodaysChangeUsd.Should().Be(0m);
        ctx.Allocation.Drifts.Should().BeEmpty();
        ctx.TopMovers.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_holding_with_fresh_snapshot_computes_pnl_and_today_change()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;

        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding
            {
                Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Equity,
                AccountType = AccountType.Taxable, Quantity = 10m, AvgCost = 150m,
            });
            c.PriceSnapshots.Add(new PriceSnapshot
            {
                Ticker = "AAPL", AssetClass = AssetClass.Equity,
                Price = 200m, PreviousClose = 190m, PercentChange = 5.26m,
                FetchedAtUtc = Now.AddMinutes(-1),
            });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        var h = ctx.Holdings.Single();
        h.Price.Should().Be(200m);
        h.MarketValueUsd.Should().Be(2000m);
        h.UnrealizedPnlUsd.Should().Be(500m);                  // 2000 - 10*150
        h.UnrealizedPnlPct.Should().BeApproximately(33.333m, 0.01m); // 500 / 1500 * 100
        h.TodaysChangePct.Should().Be(5.26m);
        h.CurrentAllocationPct.Should().Be(100m);
        h.DriftPct.Should().BeNull();

        ctx.Totals.MarketValueUsd.Should().Be(2000m);
        ctx.Totals.CostBasisUsd.Should().Be(1500m);
        ctx.Totals.UnrealizedPnlUsd.Should().Be(500m);
        ctx.Totals.TodaysChangeUsd.Should().Be(100m);          // 10*(200-190)
        ctx.Totals.TodaysChangePct.Should().BeApproximately(5.263m, 0.01m); // 100 / 1900 * 100
    }

    [Fact]
    public async Task Cad_holding_is_converted_to_usd_using_fx_rate()
    {
        var (sut, db, _) = BuildSut(cadToUsd: 0.5m); // 1 CAD = 0.50 USD
        await using var _db = db;

        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding
            {
                Ticker = "RY.TO", Name = "Royal Bank", AssetClass = AssetClass.Equity,
                AccountType = AccountType.Taxable, Quantity = 10m, AvgCost = 100m, Currency = "CAD",
            });
            c.PriceSnapshots.Add(new PriceSnapshot
            {
                Ticker = "RY.TO", AssetClass = AssetClass.Equity,
                Price = 120m, PreviousClose = 120m, PercentChange = 0m, FetchedAtUtc = Now.AddMinutes(-1),
            });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        var h = ctx.Holdings.Single();
        h.Price.Should().Be(120m);            // native CAD price is left as-is
        h.Currency.Should().Be("CAD");
        h.MarketValueUsd.Should().Be(600m);   // 10 * 120 * 0.50
        h.UnrealizedPnlUsd.Should().Be(100m); // (10*120 - 10*100) * 0.50
        ctx.Totals.MarketValueUsd.Should().Be(600m);
        ctx.Totals.CostBasisUsd.Should().Be(500m); // 10*100*0.50
    }

    [Fact]
    public async Task Drift_is_signed_difference_current_minus_target()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            c.Holdings.AddRange(
                new Holding { Ticker = "VTI", Name = "VTI", AssetClass = AssetClass.Etf,
                    AccountType = AccountType.RothIra, Quantity = 10m, AvgCost = 200m,
                    TargetAllocationPct = 70m },
                new Holding { Ticker = "BND", Name = "BND", AssetClass = AssetClass.Etf,
                    AccountType = AccountType.RothIra, Quantity = 10m, AvgCost = 80m,
                    TargetAllocationPct = 30m });
            c.PriceSnapshots.AddRange(
                new PriceSnapshot { Ticker = "VTI", AssetClass = AssetClass.Etf,
                    Price = 250m, PreviousClose = 250m, PercentChange = 0m, FetchedAtUtc = Now },
                new PriceSnapshot { Ticker = "BND", AssetClass = AssetClass.Etf,
                    Price = 75m, PreviousClose = 75m, PercentChange = 0m, FetchedAtUtc = Now });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);
        // MV: VTI = 2500, BND = 750, total = 3250
        // VTI alloc = 76.92, target 70 -> drift +6.92
        // BND alloc = 23.08, target 30 -> drift -6.92
        var vti = ctx.Holdings.Single(h => h.Ticker == "VTI");
        var bnd = ctx.Holdings.Single(h => h.Ticker == "BND");
        vti.CurrentAllocationPct!.Value.Should().BeApproximately(76.92m, 0.1m);
        vti.DriftPct!.Value.Should().BeApproximately(6.92m, 0.1m);
        bnd.DriftPct!.Value.Should().BeApproximately(-6.92m, 0.1m);

        ctx.Allocation.Drifts.Should().HaveCount(2);
        // Sorted by abs(drift) desc — both equal here, order is implementation-defined
    }

    [Fact]
    public async Task Stale_snapshot_is_included_but_flagged_and_caveated()
    {
        var (sut, db, _) = BuildSut(minFreshnessSec: 60);
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding
            {
                Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Equity,
                AccountType = AccountType.Taxable, Quantity = 10m, AvgCost = 150m,
            });
            c.PriceSnapshots.Add(new PriceSnapshot
            {
                Ticker = "AAPL", AssetClass = AssetClass.Equity,
                Price = 200m, PreviousClose = 190m, PercentChange = 5.26m,
                FetchedAtUtc = Now.AddHours(-2), // older than 60s freshness
            });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        // A stale price flagged as stale beats a holding silently vanishing from totals.
        var h = ctx.Holdings.Single();
        h.Price.Should().Be(200m);
        h.PriceIsStale.Should().BeTrue();
        h.PriceAsOfUtc.Should().Be(Now.AddHours(-2));
        h.MarketValueUsd.Should().Be(2000m);
        ctx.Totals.MarketValueUsd.Should().Be(2000m);
        ctx.TopMovers.Should().BeEmpty(); // stale % change is not "today's move"
        ctx.DataCaveats.Should().NotBeNull();
        ctx.DataCaveats!.Should().Contain(c => c.Contains("AAPL") && c.Contains("older than"));
    }

    [Fact]
    public async Task Holding_with_no_snapshot_at_all_is_caveated_as_missing()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding
            {
                Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Equity,
                AccountType = AccountType.Taxable, Quantity = 10m, AvgCost = 150m,
            });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        var h = ctx.Holdings.Single();
        h.Price.Should().BeNull();
        h.PriceIsStale.Should().BeFalse();
        ctx.Totals.MarketValueUsd.Should().Be(0m);
        ctx.DataCaveats.Should().NotBeNull();
        ctx.DataCaveats!.Should().Contain(c => c.Contains("AAPL") && c.Contains("No price"));
    }

    [Fact]
    public async Task News_is_scoped_to_tracked_tickers_plus_market_wide()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding
            {
                Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Equity,
                AccountType = AccountType.Taxable, Quantity = 1m, AvgCost = 100m,
            });
            c.NewsItems.AddRange(
                new NewsItem { Ticker = "AAPL", Headline = "apple news", Source = "s", Url = "https://e.com/1", PublishedAtUtc = Now.AddHours(-1), FetchedAtUtc = Now.AddHours(-1) },
                new NewsItem { Ticker = null, Headline = "market news", Source = "s", Url = "https://e.com/2", PublishedAtUtc = Now.AddHours(-1), FetchedAtUtc = Now.AddHours(-1) },
                // Screener-universe social noise about a name the user doesn't track:
                new NewsItem { Ticker = "GME", Headline = "to the moon", Source = "stocktwits", Url = "https://e.com/3", PublishedAtUtc = Now.AddMinutes(-5), FetchedAtUtc = Now.AddMinutes(-5) });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        ctx.RecentNews.Select(n => n.Headline).Should().BeEquivalentTo(new[] { "apple news", "market news" });
    }

    [Fact]
    public async Task Condition_trigger_scopes_news_and_sentiment_to_the_affected_ticker()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            c.Holdings.AddRange(
                new Holding { Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Equity, AccountType = AccountType.Taxable, Quantity = 1m, AvgCost = 100m },
                new Holding { Ticker = "MSFT", Name = "Microsoft", AssetClass = AssetClass.Equity, AccountType = AccountType.Taxable, Quantity = 1m, AvgCost = 100m });
            c.NewsItems.AddRange(
                new NewsItem { Ticker = "AAPL", Headline = "apple news", Source = "s", Url = "https://e.com/1", PublishedAtUtc = Now.AddHours(-1), FetchedAtUtc = Now.AddHours(-1) },
                new NewsItem { Ticker = "MSFT", Headline = "msft news", Source = "s", Url = "https://e.com/2", PublishedAtUtc = Now.AddHours(-1), FetchedAtUtc = Now.AddHours(-1) },
                new NewsItem { Ticker = null, Headline = "market news", Source = "s", Url = "https://e.com/3", PublishedAtUtc = Now.AddHours(-1), FetchedAtUtc = Now.AddHours(-1) });
            c.SaveChanges();
        }

        var trigger = new RunTrigger(RunTriggerKind.BigMove, "AAPL moved -8% today", "AAPL");
        var ctx = await sut.BuildAsync(TestTenant, trigger);

        ctx.RecentNews.Select(n => n.Headline).Should().BeEquivalentTo(new[] { "apple news", "market news" });
        ctx.Holdings.Should().HaveCount(2); // full portfolio stays — totals/drift still need it
    }

    [Fact]
    public async Task News_older_than_24h_is_excluded_and_cap_at_25_with_truncation()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            for (var i = 0; i < 30; i++)
            {
                c.NewsItems.Add(new NewsItem
                {
                    Headline = new string('x', 300),
                    Source = "src",
                    Url = $"https://example.com/n/{i}",
                    PublishedAtUtc = Now.AddHours(-i),
                    FetchedAtUtc = Now.AddHours(-i),
                });
            }
            c.NewsItems.Add(new NewsItem
            {
                Headline = "very old",
                Source = "src",
                Url = "https://example.com/old",
                PublishedAtUtc = Now.AddDays(-3),
                FetchedAtUtc = Now.AddDays(-3),
            });
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        ctx.RecentNews.Count.Should().BeLessThanOrEqualTo(25);
        ctx.RecentNews.Should().NotContain(n => n.Headline == "very old");
        ctx.RecentNews.Should().OnlyContain(n => n.Headline.Length <= 200);
    }

    [Fact]
    public async Task Top_movers_are_top_5_by_absolute_percent_change()
    {
        var (sut, db, _) = BuildSut();
        await using var _db = db;
        using (var c = db.CreateContext())
        {
            // 7 watchlist tickers so the assembler has something to sort
            var ticks = new[] { ("A", 1m), ("B", -8m), ("C", 3m), ("D", -2m), ("E", 12m), ("F", -15m), ("G", 4m) };
            foreach (var (t, pct) in ticks)
            {
                c.WatchlistItems.Add(new WatchlistItem { Ticker = t, AssetClass = AssetClass.Equity });
                c.PriceSnapshots.Add(new PriceSnapshot
                {
                    Ticker = t, AssetClass = AssetClass.Equity,
                    Price = 100m, PreviousClose = 100m, PercentChange = pct,
                    FetchedAtUtc = Now,
                });
            }
            c.SaveChanges();
        }

        var ctx = await sut.BuildAsync(TestTenant, DummyTrigger);

        ctx.TopMovers.Should().HaveCount(5);
        var tickers = ctx.TopMovers.Select(m => m.Ticker).ToArray();
        tickers.Should().BeEquivalentTo(new[] { "F", "E", "B", "G", "C" });
        ctx.TopMovers[0].Direction.Should().Be("down");
        ctx.TopMovers[1].Direction.Should().Be("up");
    }
}
