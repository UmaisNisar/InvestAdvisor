using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Swing;
using InvestAdvisor.Data.Services;
using Xunit;

namespace InvestAdvisor.Test.Swing;

public class SwingScoringServiceTests
{
    private static SwingInput Input(string ticker, IReadOnlyList<Candle> candles, AssetClass cls = AssetClass.Equity) =>
        new(ticker, ticker, "Tech", cls, candles);

    [Fact]
    public void Names_below_the_liquidity_floor_are_dropped()
    {
        var sut = new SwingScoringService();
        var p = SwingParams.Default with { MinAvgDollarVolume = 5_000_000m };

        // LIQUID: ~100 price × 2M volume. THIN: ~5 price × 1k volume = 5k $/day.
        var liquid = Input("LIQUID", SwingTestData.RegimeUpThenDip(start: 100m, volume: 2_000_000));
        var thin = Input("THIN", SwingTestData.RegimeUpThenDip(start: 5m, volume: 1_000));

        var ranked = sut.Rank(new[] { liquid, thin }, parameters: p);

        ranked.Select(r => r.Ticker).Should().Contain("LIQUID");
        ranked.Select(r => r.Ticker).Should().NotContain("THIN");
    }

    [Fact]
    public void Ranking_is_ordered_best_first_and_carries_a_trade_setup()
    {
        var sut = new SwingScoringService();
        var inputs = new[]
        {
            Input("AAA", SwingTestData.RegimeUpThenDip(start: 120m)),
            Input("BBB", SwingTestData.RegimeUpThenDip(start: 80m)),
        };

        var ranked = sut.Rank(inputs);

        ranked.Should().NotBeEmpty();
        ranked.Should().BeInDescendingOrder(r => r.CompositeScore);
        ranked.Should().OnlyContain(r => r.Qualifies); // oversold pullbacks in an uptrend
        ranked[0].Setup.StopLoss.Should().BeLessThan(ranked[0].Setup.EntryReference);
        ranked[0].CompositeScore.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public void Flat_names_with_no_signal_are_excluded()
    {
        var sut = new SwingScoringService();
        var ranked = sut.Rank(new[] { Input("FLAT", SwingTestData.Flat(260, volume: 1_000_000)) });
        ranked.Should().BeEmpty(); // ATR 0 → no setup → not scored
    }

    [Fact]
    public void Downtrend_names_are_scored_but_do_not_qualify()
    {
        var sut = new SwingScoringService();
        var ranked = sut.Rank(new[] { Input("DOWN", SwingTestData.RegimeDown()) });
        // Scored for ranking context, but the regime filter keeps it out of the actionable set.
        ranked.Should().OnlyContain(r => !r.Qualifies);
    }
}
