namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Foreign-exchange rates for converting holdings to a single base currency (USD). Cached.
/// </summary>
public interface IFxRateProvider
{
    /// <summary>Multiplier to convert an amount denominated in <paramref name="currency"/> into USD
    /// (USD = 1.0). Falls back to 1.0 if the rate can't be fetched.</summary>
    Task<decimal> GetRateToUsdAsync(string currency, CancellationToken ct = default);
}
