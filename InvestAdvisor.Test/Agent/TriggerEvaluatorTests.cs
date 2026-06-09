using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class TriggerEvaluatorTests
{
    // A Wednesday at 14:00 UTC = 10:00 ET = market open.
    private static readonly DateTime MarketOpenUtc = new(2026, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static EvaluationInput Build(
        bool manual = false,
        DateTime? lastRun = null,
        int runsToday = 0,
        IReadOnlyList<Holding>? holdings = null,
        IReadOnlyList<WatchlistItem>? watchlist = null,
        IReadOnlyDictionary<string, PriceSnapshot>? snaps = null,
        Profile? profile = null,
        RuntimeSettings? settings = null,
        DateTime? nowUtc = null) =>
        new(
            NowUtc: nowUtc ?? MarketOpenUtc,
            LastRunUtc: lastRun,
            RunsToday: runsToday,
            Profile: profile ?? new Profile
            {
                Id = Profile.SingletonId,
                DriftPctThreshold = 5m,
                SingleDayMovePctThreshold = 7m,
                RebalanceCadenceHours = 24,
            },
            Settings: settings ?? new RuntimeSettings
            {
                Id = RuntimeSettings.SingletonId,
                MinSecondsBetweenRuns = 900,
                MaxRunsPerDay = 24,
                MaxSnapshotAgeForTriggerSeconds = 600,
                MarketHoursOnly = true,
                TimeZoneId = "America/New_York",
            },
            Holdings: holdings ?? Array.Empty<Holding>(),
            Watchlist: watchlist ?? Array.Empty<WatchlistItem>(),
            LatestSnapshotsByTicker: snaps ?? new Dictionary<string, PriceSnapshot>(),
            ManualOverride: manual);

    private static PriceSnapshot Snap(string ticker, decimal price, decimal pct, DateTime when,
        AssetClass ac = AssetClass.Equity, decimal? prev = null) =>
        new() { Ticker = ticker, AssetClass = ac, Price = price, PercentChange = pct,
                PreviousClose = prev ?? price, FetchedAtUtc = when };

    [Fact]
    public void Manual_override_always_fires_Manual()
    {
        var sut = new TriggerEvaluator();
        var input = Build(manual: true, lastRun: MarketOpenUtc.AddSeconds(-1)); // would normally be blocked

        var trigger = sut.Evaluate(input);

        trigger.Should().NotBeNull();
        trigger!.Kind.Should().Be(RunTriggerKind.Manual);
    }

    [Fact]
    public void Within_min_gap_returns_null()
    {
        var sut = new TriggerEvaluator();
        var input = Build(lastRun: MarketOpenUtc.AddSeconds(-100));

        sut.Evaluate(input).Should().BeNull();
    }

    [Fact]
    public void Daily_cap_reached_returns_null()
    {
        var sut = new TriggerEvaluator();
        var input = Build(runsToday: 24);

        sut.Evaluate(input).Should().BeNull();
    }

    [Fact]
    public void Watchlist_high_target_fires_PriceTarget()
    {
        var sut = new TriggerEvaluator();
        var input = Build(
            watchlist: new[] { new WatchlistItem { Ticker = "AAPL", AssetClass = AssetClass.Equity, PriceTargetHigh = 200m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 205m, 0.5m, MarketOpenUtc) });

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.PriceTarget);
        t.Detail.Should().Contain("AAPL");
        t.Detail.Should().Contain("above");
    }

    [Fact]
    public void Watchlist_low_target_fires_PriceTarget()
    {
        var sut = new TriggerEvaluator();
        var input = Build(
            watchlist: new[] { new WatchlistItem { Ticker = "AAPL", AssetClass = AssetClass.Equity, PriceTargetLow = 180m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 175m, -2m, MarketOpenUtc) });

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.PriceTarget);
        t.Detail.Should().Contain("below");
    }

    [Fact]
    public void BigMove_fires_when_abs_pct_exceeds_threshold()
    {
        var sut = new TriggerEvaluator();
        var input = Build(
            holdings: new[] { new Holding { Ticker = "AAPL", AssetClass = AssetClass.Equity, Quantity = 1m, AvgCost = 100m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 100m, -8m, MarketOpenUtc) });

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.BigMove);
        t.Detail.Should().Contain("AAPL");
    }

    [Fact]
    public void BigMove_with_stale_snapshot_is_skipped()
    {
        var sut = new TriggerEvaluator();
        var staleTime = MarketOpenUtc.AddHours(-2);
        var input = Build(
            // lastRun an hour ago so Scheduled cadence isn't due and min-gap is satisfied
            lastRun: MarketOpenUtc.AddHours(-1),
            holdings: new[] { new Holding { Ticker = "AAPL", AssetClass = AssetClass.Equity, Quantity = 1m, AvgCost = 100m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 100m, -8m, staleTime) });

        var t = sut.Evaluate(input);

        t.Should().BeNull();
    }

    [Fact]
    public void DriftThreshold_fires_when_worst_holding_exceeds_threshold()
    {
        var sut = new TriggerEvaluator();
        var input = Build(
            holdings: new[]
            {
                new Holding { Ticker = "VTI", AssetClass = AssetClass.Etf, Quantity = 10m, AvgCost = 200m, TargetAllocationPct = 50m },
                new Holding { Ticker = "BND", AssetClass = AssetClass.Etf, Quantity = 10m, AvgCost = 80m, TargetAllocationPct = 50m },
            },
            snaps: new Dictionary<string, PriceSnapshot>
            {
                ["VTI"] = Snap("VTI", 800m, 0.1m, MarketOpenUtc, AssetClass.Etf),  // mv=8000
                ["BND"] = Snap("BND", 100m, 0.1m, MarketOpenUtc, AssetClass.Etf),  // mv=1000
            });

        var t = sut.Evaluate(input);

        // VTI alloc = 8000/9000 = 88.89%, target 50, drift +38.89 → exceeds 5% threshold
        t!.Kind.Should().Be(RunTriggerKind.DriftThreshold);
        t.Detail.Should().Contain("VTI");
    }

    [Fact]
    public void Scheduled_fires_when_cadence_elapsed()
    {
        var sut = new TriggerEvaluator();
        var input = Build(lastRun: MarketOpenUtc.AddHours(-25));

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.Scheduled);
    }

    [Fact]
    public void Scheduled_does_not_fire_when_cadence_not_elapsed()
    {
        var sut = new TriggerEvaluator();
        var input = Build(lastRun: MarketOpenUtc.AddHours(-2));

        sut.Evaluate(input).Should().BeNull();
    }

    [Fact]
    public void Crypto_BigMove_still_fires_when_market_closed()
    {
        var sut = new TriggerEvaluator();
        var saturday = new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc); // weekend, market closed
        var input = Build(
            nowUtc: saturday,
            holdings: new[] { new Holding { Ticker = "BTC", AssetClass = AssetClass.Crypto, Quantity = 1m, AvgCost = 50000m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["BTC"] = Snap("BTC", 60000m, 10m, saturday, AssetClass.Crypto) });

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.BigMove);
    }

    [Fact]
    public void Equity_BigMove_suppressed_when_market_closed_and_MarketHoursOnly_true()
    {
        var sut = new TriggerEvaluator();
        var saturday = new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc);
        var input = Build(
            nowUtc: saturday,
            holdings: new[] { new Holding { Ticker = "AAPL", AssetClass = AssetClass.Equity, Quantity = 1m, AvgCost = 100m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 100m, -10m, saturday) },
            lastRun: saturday.AddMinutes(-30)); // inside cadence, so no Scheduled fallback

        // No equity-side trigger should fire. Scheduled may fire if cadence elapsed, but min-gap suppresses it.
        var t = sut.Evaluate(input);

        t.Should().BeNull();
    }

    [Fact]
    public void Priority_PriceTarget_wins_over_BigMove()
    {
        var sut = new TriggerEvaluator();
        var input = Build(
            holdings: new[] { new Holding { Ticker = "AAPL", AssetClass = AssetClass.Equity, Quantity = 1m, AvgCost = 100m } },
            watchlist: new[] { new WatchlistItem { Ticker = "AAPL", AssetClass = AssetClass.Equity, PriceTargetHigh = 99m } },
            snaps: new Dictionary<string, PriceSnapshot> { ["AAPL"] = Snap("AAPL", 100m, -8m, MarketOpenUtc) });

        var t = sut.Evaluate(input);

        t!.Kind.Should().Be(RunTriggerKind.PriceTarget);
    }
}
