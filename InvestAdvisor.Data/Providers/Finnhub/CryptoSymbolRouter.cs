using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Finnhub;

/// <summary>
/// Maps the user's local ticker (e.g. "BTC") to the exchange-qualified symbol Finnhub
/// expects for crypto (e.g. "BINANCE:BTCUSDT"). For non-crypto assets the ticker is
/// returned unchanged. Returns null when the user's ticker already looks exchange-qualified.
/// </summary>
public sealed class CryptoSymbolRouter(IOptions<FinnhubOptions> options)
{
    private readonly FinnhubOptions _opts = options.Value;

    public string RouteSymbol(string ticker, AssetClass assetClass)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker cannot be empty.", nameof(ticker));

        if (assetClass != AssetClass.Crypto)
            return ticker.ToUpperInvariant();

        // If already exchange-qualified, pass through.
        if (ticker.Contains(':'))
            return ticker.ToUpperInvariant();

        return $"{_opts.CryptoExchangePrefix}:{ticker.ToUpperInvariant()}{_opts.CryptoQuoteSuffix}";
    }
}
