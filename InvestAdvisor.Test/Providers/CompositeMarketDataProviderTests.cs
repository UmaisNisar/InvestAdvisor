using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers;
using InvestAdvisor.Data.Providers.Finnhub;
using InvestAdvisor.Data.Providers.Yahoo;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class CompositeMarketDataProviderTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("BTC", "bitcoin")]
    [InlineData("eth", "ethereum")]        // case-insensitive
    [InlineData("SOL", "solana")]
    [InlineData("WEIRDCOIN", "weirdcoin")] // unknown → lowercase fallback
    public void CryptoIds_maps_known_and_falls_back_to_lowercase(string ticker, string expected)
    {
        CryptoIds.ToCoinGeckoId(ticker).Should().Be(expected);
    }

    private sealed record Sut(
        CompositeMarketDataProvider Provider,
        StubHttpMessageHandler Finnhub,
        StubHttpMessageHandler Yahoo);

    private static Sut Build(string finnhubBody = "{}", string yahooBody = "{}", CryptoMarket? crypto = null)
    {
        var finnhubHandler = new StubHttpMessageHandler { ResponseBody = finnhubBody };
        var yahooHandler = new StubHttpMessageHandler { ResponseBody = yahooBody };
        var fopts = Options.Create(new FinnhubOptions { ApiKey = "test", BaseUrl = "https://finnhub.io" });
        var limiter = Substitute.For<IRateLimiter>();
        limiter.WaitAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        var clock = new FakeSystemClock(Now);
        var finnhub = new FinnhubMarketDataProvider(
            new HttpClient(finnhubHandler) { BaseAddress = new Uri("https://finnhub.io/") },
            limiter, new CryptoSymbolRouter(fopts), clock, fopts);
        var yahoo = new YahooQuoteProvider(
            new HttpClient(yahooHandler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") }, clock);
        var coinGecko = Substitute.For<ICryptoMarketProvider>();
        if (crypto is not null)
            coinGecko.GetMarketsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult<IReadOnlyList<CryptoMarket>>(new[] { crypto }));
        return new Sut(new CompositeMarketDataProvider(finnhub, yahoo, coinGecko, clock), finnhubHandler, yahooHandler);
    }

    [Fact]
    public async Task Us_equity_routes_to_finnhub()
    {
        var sut = Build(finnhubBody: """{ "c": 200.0, "dp": 2.5, "pc": 195.0 }""");

        var quote = await sut.Provider.GetQuoteAsync("AAPL", AssetClass.Equity);

        quote!.Price.Should().Be(200m);
        sut.Finnhub.CallCount.Should().Be(1);
        sut.Yahoo.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Dotted_ticker_routes_to_yahoo()
    {
        var sut = Build(yahooBody:
            """{"chart":{"result":[{"meta":{"regularMarketPrice":154.65,"chartPreviousClose":150.0,"currency":"CAD"}}]}}""");

        var quote = await sut.Provider.GetQuoteAsync("SHOP.TO", AssetClass.Equity);

        quote!.Price.Should().Be(154.65m);
        sut.Yahoo.CallCount.Should().Be(1);
        sut.Finnhub.CallCount.Should().Be(0); // dot routed it straight to Yahoo
    }

    [Fact]
    public async Task Crypto_routes_to_coingecko_with_prev_close_from_24h_change()
    {
        var sut = Build(crypto: new CryptoMarket("bitcoin", "BTC", 60000m, 1m, 1m, 1m, 20m));

        var quote = await sut.Provider.GetQuoteAsync("BTC", AssetClass.Crypto);

        quote!.Price.Should().Be(60000m);
        quote.PercentChange.Should().Be(20m);
        quote.PreviousClose.Should().BeApproximately(50000m, 0.01m); // 60000 / 1.20
        sut.Finnhub.CallCount.Should().Be(0);
        sut.Yahoo.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Finnhub_empty_falls_back_to_yahoo_for_us_ticker()
    {
        var sut = Build(
            finnhubBody: """{ "c": 0 }""", // no data
            yahooBody: """{"chart":{"result":[{"meta":{"regularMarketPrice":99.0,"chartPreviousClose":98.0}}]}}""");

        var quote = await sut.Provider.GetQuoteAsync("AAPL", AssetClass.Equity);

        quote!.Price.Should().Be(99m);
        sut.Finnhub.CallCount.Should().Be(1);
        sut.Yahoo.CallCount.Should().Be(1); // fell back
    }
}
