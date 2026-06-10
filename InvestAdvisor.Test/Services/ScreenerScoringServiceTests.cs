using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class ScreenerScoringServiceTests
{
    private static ScreenerScoringService BuildSut(
        SqliteFixture db, RuntimeSettings settings, IReadOnlyDictionary<string, TickerSentiment> sentiment)
    {
        var store = Substitute.For<IRuntimeSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<RuntimeSettings>(settings));
        var sentimentSvc = Substitute.For<ISentimentScoringService>();
        sentimentSvc.GetTickerSentimentAsync(Arg.Any<CancellationToken>()).Returns(sentiment);
        return new ScreenerScoringService(db.Factory, store, sentimentSvc);
    }

    [Fact]
    public async Task Sentiment_factor_drives_ranking_when_it_is_the_only_weight()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Stocks.AddRange(
                new Stock { Ticker = "AAPL", AssetClass = AssetClass.Equity, IsActive = true },
                new Stock { Ticker = "TSLA", AssetClass = AssetClass.Equity, IsActive = true });
            c.SaveChanges();
        }
        var settings = new RuntimeSettings
        {
            WeightValuation = 0, WeightGrowth = 0, WeightQuality = 0,
            WeightAnalyst = 0, WeightInsider = 0, WeightMomentum = 0, WeightSentiment = 100,
        };
        var sentiment = new Dictionary<string, TickerSentiment>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new(0.9m, 12, "bullish"),
            ["TSLA"] = new(-0.9m, 8, "bearish"),
        };
        var sut = BuildSut(db, settings, sentiment);

        var ranked = await sut.RankAsync(AssetClass.Equity);

        ranked.Should().HaveCount(2);
        ranked[0].Ticker.Should().Be("AAPL");           // bullish sentiment ranks first
        ranked[0].Factors.Sentiment.Should().NotBeNull();
        ranked[0].Factors.Sentiment.Should().BeGreaterThan(ranked[1].Factors.Sentiment!.Value);
    }

    [Fact]
    public async Task Sentiment_factor_is_null_when_no_sentiment_data()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Stocks.Add(new Stock { Ticker = "AAPL", AssetClass = AssetClass.Equity, IsActive = true });
            c.SaveChanges();
        }
        var sut = BuildSut(db, new RuntimeSettings(),
            new Dictionary<string, TickerSentiment>(StringComparer.OrdinalIgnoreCase));

        var ranked = await sut.RankAsync(AssetClass.Equity);

        ranked.Should().ContainSingle();
        ranked[0].Factors.Sentiment.Should().BeNull(); // no data -> factor absent, not zero
    }
}
