using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class SocialRefreshServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ISocialFeedProvider Provider(NewsSource channel, params SocialPost[] posts)
    {
        var p = Substitute.For<ISocialFeedProvider>();
        p.Channel.Returns(channel);
        p.GetTickerPostsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(posts.ToList());
        return p;
    }

    private static SocialPost Post(string ticker, string url, NewsSource channel) =>
        new(ticker, $"{ticker} chatter", channel.ToString(), url, Now, channel);

    [Fact]
    public async Task Persists_posts_with_channel_and_unscored()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Stocks.Add(new Stock { Ticker = "AAPL", AssetClass = AssetClass.Equity });
            c.SaveChanges();
        }
        var provider = Provider(NewsSource.StockTwits, Post("AAPL", "https://st/1", NewsSource.StockTwits));
        var sut = new SocialRefreshService(db.Factory, new[] { provider }, new FakeSystemClock(Now));

        var written = await sut.RefreshAsync();

        written.Should().Be(1);
        using var verify = db.CreateContext();
        var row = await verify.NewsItems.SingleAsync();
        row.Channel.Should().Be(NewsSource.StockTwits);
        row.Ticker.Should().Be("AAPL");
        row.SentimentScoredAtUtc.Should().BeNull(); // left for the scorer
    }

    [Fact]
    public async Task Dedups_existing_urls()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Stocks.Add(new Stock { Ticker = "AAPL", AssetClass = AssetClass.Equity });
            c.NewsItems.Add(new NewsItem
            {
                Ticker = "AAPL", Headline = "old", Source = "StockTwits", Url = "https://st/1",
                Channel = NewsSource.StockTwits, PublishedAtUtc = Now.AddDays(-1), FetchedAtUtc = Now.AddDays(-1),
            });
            c.SaveChanges();
        }
        var provider = Provider(NewsSource.StockTwits,
            Post("AAPL", "https://st/1", NewsSource.StockTwits),  // dup
            Post("AAPL", "https://st/2", NewsSource.StockTwits));  // new
        var sut = new SocialRefreshService(db.Factory, new[] { provider }, new FakeSystemClock(Now));

        var written = await sut.RefreshAsync();

        written.Should().Be(1);
        using var verify = db.CreateContext();
        (await verify.NewsItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Respects_cadence_skips_when_recent_social_row_exists()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Stocks.Add(new Stock { Ticker = "AAPL", AssetClass = AssetClass.Equity });
            c.NewsItems.Add(new NewsItem
            {
                Ticker = "AAPL", Headline = "recent", Source = "Reddit", Url = "https://r/1",
                Channel = NewsSource.Reddit, PublishedAtUtc = Now.AddMinutes(-5), FetchedAtUtc = Now.AddMinutes(-5),
            });
            c.SaveChanges();
        }
        var provider = Provider(NewsSource.StockTwits, Post("AAPL", "https://st/9", NewsSource.StockTwits));
        var sut = new SocialRefreshService(db.Factory, new[] { provider }, new FakeSystemClock(Now));

        var written = await sut.RefreshAsync();

        written.Should().Be(0);
        await provider.DidNotReceive().GetTickerPostsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pulls_tracked_tickers_even_without_universe()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.Holdings.Add(new Holding { Ticker = "TSLA", AssetClass = AssetClass.Equity, Quantity = 1m, AvgCost = 1m });
            c.SaveChanges();
        }
        var provider = Provider(NewsSource.StockTwits, Post("TSLA", "https://st/t", NewsSource.StockTwits));
        var sut = new SocialRefreshService(db.Factory, new[] { provider }, new FakeSystemClock(Now));

        var written = await sut.RefreshAsync();

        written.Should().Be(1);
        await provider.Received().GetTickerPostsAsync("TSLA", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_providers_returns_zero()
    {
        await using var db = new SqliteFixture();
        var sut = new SocialRefreshService(db.Factory, Array.Empty<ISocialFeedProvider>(), new FakeSystemClock(Now));

        (await sut.RefreshAsync()).Should().Be(0);
    }
}
