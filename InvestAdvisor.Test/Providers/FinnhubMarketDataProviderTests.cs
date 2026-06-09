using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Finnhub;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using InvestAdvisor.Core.Abstractions;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class FinnhubMarketDataProviderTests
{
    private static (FinnhubMarketDataProvider sut, StubHttpMessageHandler handler) BuildSut(string responseBody)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://finnhub.io/") };
        var opts = Options.Create(new FinnhubOptions { ApiKey = "test", BaseUrl = "https://finnhub.io" });
        var limiter = Substitute.For<IRateLimiter>();
        limiter.WaitAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        var clock = new FakeSystemClock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var router = new CryptoSymbolRouter(opts);
        return (new FinnhubMarketDataProvider(http, limiter, router, clock, opts), handler);
    }

    [Fact]
    public async Task Equity_quote_maps_finnhub_fields()
    {
        var (sut, handler) = BuildSut("""
            { "c": 200.0, "d": 5.0, "dp": 2.5, "h": 201, "l": 195, "o": 195, "pc": 195.0, "t": 1717209600 }
            """);

        var quote = await sut.GetQuoteAsync("aapl", AssetClass.Equity);

        quote.Should().NotBeNull();
        quote!.Ticker.Should().Be("AAPL");
        quote.Price.Should().Be(200m);
        quote.PreviousClose.Should().Be(195m);
        quote.PercentChange.Should().Be(2.5m);
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Contain("/quote?symbol=AAPL&token=test");
    }

    [Fact]
    public async Task Equity_quote_returning_zero_price_is_treated_as_no_data()
    {
        var (sut, _) = BuildSut("""{ "c": 0, "d": 0, "dp": 0, "pc": 0, "t": 0 }""");

        var quote = await sut.GetQuoteAsync("XYZQ", AssetClass.Equity);

        quote.Should().BeNull();
    }

    [Fact]
    public async Task Crypto_quote_uses_candle_endpoint_and_routed_symbol()
    {
        var (sut, handler) = BuildSut("""
            { "s": "ok", "c": [60000, 61000, 62000], "o": [59500, 60000, 61000], "h": [60500, 61200, 62500], "l": [59000, 59800, 60500], "t": [1, 2, 3], "v": [1, 1, 1] }
            """);

        var quote = await sut.GetQuoteAsync("BTC", AssetClass.Crypto);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(62000m);
        quote.PreviousClose.Should().Be(61000m);
        quote.PercentChange.Should().BeApproximately(1.639m, 0.01m);
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Contain("/crypto/candle");
        handler.LastRequest!.RequestUri!.Query.Should().Contain("BINANCE%3ABTCUSDT");
    }

    [Fact]
    public async Task Missing_api_key_throws()
    {
        var handler = new StubHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://finnhub.io/") };
        var opts = Options.Create(new FinnhubOptions { ApiKey = "" });
        var limiter = Substitute.For<IRateLimiter>();
        limiter.WaitAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        var clock = new FakeSystemClock(DateTime.UtcNow);
        var router = new CryptoSymbolRouter(opts);
        var sut = new FinnhubMarketDataProvider(http, limiter, router, clock, opts);

        var act = () => sut.GetQuoteAsync("AAPL", AssetClass.Equity);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
