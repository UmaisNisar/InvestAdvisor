using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.HackerNews;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class HackerNewsProviderTests
{
    private static HackerNewsProvider BuildSut(string body, bool enabled = true, int minPoints = 0)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = body };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://hn.algolia.com/") };
        return new HackerNewsProvider(http, Options.Create(new HackerNewsOptions { Enabled = enabled, MinPoints = minPoints }));
    }

    [Fact]
    public async Task Maps_story_title_and_comment_text()
    {
        var sut = BuildSut("""
        {
          "hits": [
            { "objectID": "100", "title": "Nvidia ships new GPU", "points": 250, "created_at_i": 1749556800 },
            { "objectID": "101", "comment_text": "NVDA margins are insane", "points": null, "created_at_i": 1749553200 }
          ]
        }
        """);

        var posts = await sut.GetTickerPostsAsync("nvda");

        posts.Should().HaveCount(2);
        posts[0].Ticker.Should().Be("NVDA");
        posts[0].Text.Should().Be("Nvidia ships new GPU");
        posts[0].Channel.Should().Be(NewsSource.HackerNews);
        posts[0].Source.Should().Be("Hacker News");
        posts[0].Url.Should().Be("https://news.ycombinator.com/item?id=100");
        posts[0].CreatedAtUtc.Should().Be(new DateTime(2025, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        posts[1].Text.Should().Be("NVDA margins are insane"); // comment fallback, null points treated as 0
    }

    [Fact]
    public async Task Filters_below_min_points()
    {
        var sut = BuildSut("""
        {
          "hits": [
            { "objectID": "1", "title": "low signal", "points": 1, "created_at_i": 1749556800 },
            { "objectID": "2", "title": "high signal", "points": 80, "created_at_i": 1749556800 }
          ]
        }
        """, minPoints: 10);

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().ContainSingle();
        posts[0].Text.Should().Be("high signal");
    }

    [Fact]
    public async Task Disabled_returns_empty_without_calling()
    {
        var handler = new StubHttpMessageHandler { ResponseBody = "{}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://hn.algolia.com/") };
        var sut = new HackerNewsProvider(http, Options.Create(new HackerNewsOptions { Enabled = false }));

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_payload_degrades_to_empty()
    {
        var sut = BuildSut("garbage");

        (await sut.GetTickerPostsAsync("AAPL")).Should().BeEmpty();
    }
}
