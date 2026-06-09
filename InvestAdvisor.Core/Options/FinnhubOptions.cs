namespace InvestAdvisor.Core.Options;

public sealed class FinnhubOptions
{
    public const string SectionName = "Finnhub";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://finnhub.io/api/v1";
    public int RequestsPerMinute { get; set; } = 60;
    public string CryptoExchangePrefix { get; set; } = "BINANCE";
    public string CryptoQuoteSuffix { get; set; } = "USDT";
    public int TimeoutSeconds { get; set; } = 30;
}
