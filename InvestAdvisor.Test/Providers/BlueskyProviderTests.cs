using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Bluesky;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class BlueskyProviderTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

    private const string SearchBody = """
    {
      "posts": [
        {
          "uri": "at://did:plc:abc/app.bsky.feed.post/xyz",
          "author": { "handle": "alice.bsky.social", "did": "did:plc:abc" },
          "record": { "text": "$AAPL breaking out", "createdAt": "2026-06-10T12:00:00Z" },
          "indexedAt": "2026-06-10T12:01:00Z"
        },
        {
          "uri": "at://did:plc:def/app.bsky.feed.post/qqq",
          "author": { "handle": "bob.bsky.social", "did": "did:plc:def" },
          "record": { "text": "", "createdAt": "2026-06-10T11:00:00Z" },
          "indexedAt": "2026-06-10T11:01:00Z"
        }
      ]
    }
    """;

    [Fact]
    public async Task Unauthenticated_search_maps_posts_and_builds_permalink()
    {
        var handler = new StubHttpMessageHandler { ResponseBody = SearchBody };
        var http = new HttpClient(handler);
        var sut = new BlueskyProvider(http, Options.Create(new BlueskyOptions { Enabled = true }),
            new FakeSystemClock(Now));

        var posts = await sut.GetTickerPostsAsync("aapl");

        posts.Should().ContainSingle(); // blank-text post dropped
        posts[0].Ticker.Should().Be("AAPL");
        posts[0].Text.Should().Be("$AAPL breaking out");
        posts[0].Channel.Should().Be(NewsSource.Bluesky);
        posts[0].Source.Should().Be("Bluesky");
        posts[0].Url.Should().Be("https://bsky.app/profile/alice.bsky.social/post/xyz");
        posts[0].CreatedAtUtc.Should().Be(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));

        // Cashtag query and no auth header when no credentials.
        handler.LastRequest!.RequestUri!.Query.Should().Contain(Uri.EscapeDataString("$AAPL"));
        handler.LastRequest.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task With_credentials_creates_session_then_sends_bearer()
    {
        var handler = new RoutingHttpMessageHandler()
            .When("createSession", """{ "accessJwt": "jwt-abc", "refreshJwt": "r" }""")
            .When("searchPosts", SearchBody);
        var http = new HttpClient(handler);
        var sut = new BlueskyProvider(http, Options.Create(new BlueskyOptions
        {
            Enabled = true, Identifier = "me.bsky.social", AppPassword = "app-pass",
        }), new FakeSystemClock(Now));

        var posts = await sut.GetTickerPostsAsync("AAPL");

        posts.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("createSession");
        handler.Requests[1].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[1].Headers.Authorization!.Parameter.Should().Be("jwt-abc");
    }

    [Fact]
    public async Task Caches_session_token_across_calls()
    {
        var handler = new RoutingHttpMessageHandler()
            .When("createSession", """{ "accessJwt": "jwt-abc" }""")
            .When("searchPosts", """{ "posts": [] }""");
        var http = new HttpClient(handler);
        var sut = new BlueskyProvider(http, Options.Create(new BlueskyOptions
        {
            Enabled = true, Identifier = "me", AppPassword = "p",
        }), new FakeSystemClock(Now));

        await sut.GetTickerPostsAsync("AAPL");
        await sut.GetTickerPostsAsync("MSFT");

        handler.Requests.Count(r => r.RequestUri!.ToString().Contains("createSession")).Should().Be(1);
        handler.Requests.Count(r => r.RequestUri!.ToString().Contains("searchPosts")).Should().Be(2);
    }

    [Fact]
    public async Task Disabled_returns_empty_without_calling()
    {
        var handler = new StubHttpMessageHandler { ResponseBody = SearchBody };
        var http = new HttpClient(handler);
        var sut = new BlueskyProvider(http, Options.Create(new BlueskyOptions { Enabled = false }),
            new FakeSystemClock(Now));

        (await sut.GetTickerPostsAsync("AAPL")).Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }
}
