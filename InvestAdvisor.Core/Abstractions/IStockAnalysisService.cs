namespace InvestAdvisor.Core.Abstractions;

/// <summary>
/// Runs the LLM "expensive pass": analyses only today's shortlist — the top opportunities and
/// top risks from the composite ranking — so spend stays roughly flat regardless of universe
/// size. Idempotent per day (skips any ticker already analysed today).
/// </summary>
public interface IStockAnalysisService
{
    Task<int> AnalyzeShortlistAsync(CancellationToken ct = default);
}
