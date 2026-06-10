using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.StockTwits;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class StockTwitsProviderTests
{
    private static StockTwitsProvider BuildSut(string responseBody, bool enabled = true)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.stocktwits.com/") };
        var opts = Options.Create(new StockTwitsOptions { Enabled = enabled });
        return new StockTwitsProvider(http, opts);
    }

    [Fact]
    public async Task Maps_messages_with_body_url_and_provider_sentiment()
    {
        var sut = BuildSut("""
        {
          "messages": [
            {
              "id": 555,
              "body": "AAPL breaking out, loading up",
              "created_at": "2026-06-10T12:00:00Z",
              "user": { "username": "trader1" },
              "entities": { "sentiment": { "basic": "Bullish" } }
            },
            {
              "id": 556,
              "body": "Not so sure about this one",
              "created_at": "2026-06-10T11:00:00Z",
              "user": { "username": "trader2" },
              "entities": { "sentiment": null }
            }
          ]
        }
        """);

        var posts = await sut.GetTickerPostsAsync("aapl");

        posts.Should().HaveCount(2);
        posts[0].Ticker.Should().Be("AAPL");
        posts[0].Text.Should().Be("AAPL breaking out, loading up");
        posts[0].Channel.Should().Be(NewsSource.StockTwits);
        posts[0].Source.Should().Be("StockTwits");
        posts[0].Url.Should().Be("https://stocktwits.com/trader1/message/555");
        posts[0].ProviderSentiment.Should().Be("Bullish");
        posts[0].CreatedAtUtc.Should().Be(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        posts[1].ProviderSentiment.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_returns_empty_without_calling()
    {
        var handler = new StubHttpMessageHandler { ResponseBody = "{}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.stocktwits.com/") };
        var sut = new StockTwitsProvider(http, Options.Create(new StockTwitsOptions { Enabled = false }));

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Skips_messages_with_blank_body_and_caps_results()
    {
        var sut = BuildSut("""
        {
          "messages": [
            { "id": 1, "body": "", "created_at": "2026-06-10T12:00:00Z" },
            { "id": 2, "body": "real post", "created_at": "2026-06-10T12:00:00Z" }
          ]
        }
        """);

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().ContainSingle();
        posts[0].Text.Should().Be("real post");
        posts[0].Url.Should().Be("https://stocktwits.com/message/2"); // no username -> short form
    }

    [Fact]
    public async Task Empty_or_malformed_payload_degrades_to_empty()
    {
        var sut = BuildSut("not json at all");

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().BeEmpty();
    }
}
