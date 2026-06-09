using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Finnhub;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class CryptoSymbolRouterTests
{
    private static CryptoSymbolRouter BuildSut() => new(Options.Create(new FinnhubOptions
    {
        CryptoExchangePrefix = "BINANCE",
        CryptoQuoteSuffix = "USDT",
    }));

    [Theory]
    [InlineData("AAPL", AssetClass.Equity, "AAPL")]
    [InlineData("aapl", AssetClass.Equity, "AAPL")]
    [InlineData("vti", AssetClass.Etf, "VTI")]
    public void Non_crypto_is_passed_through_uppercased(string ticker, AssetClass ac, string expected)
    {
        BuildSut().RouteSymbol(ticker, ac).Should().Be(expected);
    }

    [Theory]
    [InlineData("BTC", "BINANCE:BTCUSDT")]
    [InlineData("eth", "BINANCE:ETHUSDT")]
    public void Crypto_gets_exchange_prefix_and_suffix(string ticker, string expected)
    {
        BuildSut().RouteSymbol(ticker, AssetClass.Crypto).Should().Be(expected);
    }

    [Fact]
    public void Crypto_already_qualified_is_passed_through_uppercased()
    {
        BuildSut().RouteSymbol("KRAKEN:BTCUSD", AssetClass.Crypto).Should().Be("KRAKEN:BTCUSD");
    }

    [Fact]
    public void Empty_ticker_throws()
    {
        var act = () => BuildSut().RouteSymbol(" ", AssetClass.Equity);
        act.Should().Throw<ArgumentException>();
    }
}
