using System.Net;
using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Reddit;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class RedditProviderTests
{
    private static RedditProvider BuildSut(RoutingHttpMessageHandler handler, RedditOptions? opts = null)
    {
        var http = new HttpClient(handler);
        var clock = new FakeSystemClock(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        return new RedditProvider(http, Options.Create(opts ?? new RedditOptions
        {
            ClientId = "cid", ClientSecret = "secret", MinScore = 5,
        }), clock);
    }

    private static RoutingHttpMessageHandler TokenThenSearch(string searchBody) =>
        new RoutingHttpMessageHandler()
            .When("access_token", """{ "access_token": "tok123", "expires_in": 3600 }""")
            .When("/search", searchBody);

    [Fact]
    public async Task Fetches_token_then_maps_search_results()
    {
        var handler = TokenThenSearch("""
        {
          "data": {
            "children": [
              {
                "data": {
                  "title": "AAPL earnings look strong",
                  "selftext": "Margins expanding.",
                  "permalink": "/r/stocks/comments/abc/aapl/",
                  "subreddit": "stocks",
                  "score": 42,
                  "created_utc": 1749556800
                }
              }
            ]
          }
        }
        """);
        var sut = BuildSut(handler);

        var posts = await sut.GetTickerPostsAsync("aapl");

        posts.Should().ContainSingle();
        posts[0].Ticker.Should().Be("AAPL");
        posts[0].Text.Should().Be("AAPL earnings look strong. Margins expanding.");
        posts[0].Channel.Should().Be(NewsSource.Reddit);
        posts[0].Source.Should().Be("r/stocks");
        posts[0].Url.Should().Be("https://www.reddit.com/r/stocks/comments/abc/aapl/");

        // First call obtains the token (Basic auth), second carries the bearer token.
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.Requests[1].Headers.Authorization!.Scheme.Should().Be("bearer");
        handler.Requests[1].Headers.Authorization!.Parameter.Should().Be("tok123");
    }

    [Fact]
    public async Task Filters_posts_below_min_score()
    {
        var handler = TokenThenSearch("""
        {
          "data": { "children": [
            { "data": { "title": "low", "permalink": "/r/x/1", "subreddit": "x", "score": 2, "created_utc": 1749556800 } },
            { "data": { "title": "high", "permalink": "/r/x/2", "subreddit": "x", "score": 9, "created_utc": 1749556800 } }
          ] }
        }
        """);
        var sut = BuildSut(handler);

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().ContainSingle();
        posts[0].Text.Should().Be("high");
    }

    [Fact]
    public async Task Caches_token_across_calls()
    {
        var handler = TokenThenSearch("""{ "data": { "children": [] } }""");
        var sut = BuildSut(handler);

        await sut.GetTickerPostsAsync("AAPL");
        await sut.GetTickerPostsAsync("MSFT");

        // One token request, two searches.
        handler.Requests.Count(r => r.RequestUri!.ToString().Contains("access_token")).Should().Be(1);
        handler.Requests.Count(r => r.RequestUri!.ToString().Contains("/search")).Should().Be(2);
    }

    [Fact]
    public async Task Not_configured_returns_empty_without_calling()
    {
        var handler = TokenThenSearch("""{ "data": { "children": [] } }""");
        var sut = BuildSut(handler, new RedditOptions { ClientId = "", ClientSecret = "" });

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_failure_degrades_to_empty()
    {
        var handler = new RoutingHttpMessageHandler()
            .When("access_token", "nope", HttpStatusCode.Unauthorized);
        var sut = BuildSut(handler);

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().BeEmpty();
    }
}
