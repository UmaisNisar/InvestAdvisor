namespace InvestAdvisor.Core.Models;

/// <summary>Portfolio market value at one moment, in USD (display conversion happens in the UI).</summary>
public sealed record PortfolioValuePoint(DateTime TimeUtc, decimal ValueUsd);

/// <summary>
/// The portfolio's value over a lookback window, reconstructed from per-ticker price history with
/// today's share counts and FX rates held constant. That makes it a "market movement of the current
/// portfolio" line, not a true account-value history (deposits/trades inside the window aren't
/// modeled) — the standard simplification when there's no transaction ledger.
/// <paramref name="MissingTickers"/> lists holdings excluded because no history came back for them.
/// </summary>
public sealed record PortfolioValueHistory(
    IReadOnlyList<PortfolioValuePoint> Points,
    IReadOnlyList<string> MissingTickers);
