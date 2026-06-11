using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class NewsRefreshServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    private static NewsHeadline Headline(string ticker, string url) =>
        new(ticker, $"{ticker} headline", "src", url, Now.AddHours(-1));

    [Fact]
    public async Task Falls_through_to_the_next_provider_when_the_first_has_no_coverage()
    {
        await using var db = new SqliteFixture();
        var clock = new FakeSystemClock(Now);

        // Finnhub-style primary: covers AAPL, returns nothing for the Canadian ticker.
        var primary = Substitute.For<INewsProvider>();
        primary.GetTickerNewsAsync("AAPL", Arg.Any<CancellationToken>())
            .Returns(new[] { Headline("AAPL", "https://n.example/aapl") });
        primary.GetTickerNewsAsync("MU.TO", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<NewsHeadline>());
        primary.GetMarketNewsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new NewsHeadline(null, "market", "src", "https://n.example/mkt", Now.AddHours(-1)) });

        // Yahoo-style fallback: covers the Canadian ticker, no market news.
        var fallback = Substitute.For<INewsProvider>();
        fallback.GetTickerNewsAsync("MU.TO", Arg.Any<CancellationToken>())
            .Returns(new[] { Headline("MU.TO", "https://n.example/muto") });
        fallback.GetMarketNewsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<NewsHeadline>());

        var sut = new NewsRefreshService(db.Factory, new[] { primary, fallback }, clock);

        var written = await sut.RefreshAsync(new[]
        {
            new TickerSpec("AAPL", Core.Enums.AssetClass.Equity),
            new TickerSpec("MU.TO", Core.Enums.AssetClass.Equity),
        });

        written.Should().Be(3); // AAPL (primary) + MU.TO (fallback) + market (primary)
        using var c = db.CreateContext();
        c.NewsItems.Select(n => n.Ticker).ToList().Should().BeEquivalentTo(new[] { "AAPL", "MU.TO", null });

        // The fallback was only consulted for the ticker the primary couldn't answer.
        await fallback.DidNotReceive().GetTickerNewsAsync("AAPL", Arg.Any<CancellationToken>());
    }
}
