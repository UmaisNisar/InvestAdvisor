using FluentAssertions;
using InvestAdvisor.Data.Providers.Yahoo;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class YahooQuoteProviderTests
{
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
}
