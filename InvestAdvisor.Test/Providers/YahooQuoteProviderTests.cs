using FluentAssertions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Providers.Yahoo;
using InvestAdvisor.Test.TestHelpers;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class YahooQuoteProviderTests
{
    private static YahooQuoteProvider BuildSut(string responseBody)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") };
        var clock = new FakeSystemClock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        return new YahooQuoteProvider(http, clock);
    }

    [Theory]
    [InlineData("IDIV.B.TO", "IDIV-B.TO")] // share-class dot → dash, but exchange suffix kept
    [InlineData("SHOP.TO", "SHOP.TO")]      // plain Toronto ticker unchanged
    [InlineData("RY.TO", "RY.TO")]
    [InlineData("TOI.V", "TOI.V")]          // TSX Venture
    [InlineData("BHP.AX", "BHP.AX")]        // ASX (Australia)
    [InlineData("BRK.B", "BRK-B")]          // US share class, no exchange suffix → all dots dashed
    [InlineData("AAPL", "AAPL")]            // plain US ticker, unchanged
    public void ToYahooSymbol_keeps_exchange_suffix_but_dashes_share_classes(string input, string expected)
    {
        YahooQuoteProvider.ToYahooSymbol(input).Should().Be(expected);
    }

    [Fact]
    public async Task Daily_change_uses_previousClose_not_chartPreviousClose()
    {
        // chartPreviousClose is the close before the 5-day range (~a week ago); previousClose is the
        // genuine prior session. The daily change must be computed off previousClose, otherwise it
        // reports a ~5-day return. Here: prior session 99 → 100 is +1.01%, not (100-90)/90 = +11.1%.
        var sut = BuildSut("""
            { "chart": { "result": [ { "meta": {
                "regularMarketPrice": 100.0,
                "chartPreviousClose": 90.0,
                "previousClose": 99.0,
                "currency": "USD"
            } } ] } }
            """);

        var quote = await sut.GetQuoteAsync("RY.TO", AssetClass.Equity);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(100m);
        quote.PreviousClose.Should().Be(99m);
        quote.PercentChange.Should().BeApproximately(1.0101m, 0.001m);
    }

    [Fact]
    public async Task Daily_change_falls_back_to_chartPreviousClose_when_previousClose_absent()
    {
        var sut = BuildSut("""
            { "chart": { "result": [ { "meta": {
                "regularMarketPrice": 100.0,
                "chartPreviousClose": 95.0,
                "currency": "USD"
            } } ] } }
            """);

        var quote = await sut.GetQuoteAsync("RY.TO", AssetClass.Equity);

        quote.Should().NotBeNull();
        quote!.PreviousClose.Should().Be(95m);
        quote.PercentChange.Should().BeApproximately(5.2632m, 0.001m);
    }
}
