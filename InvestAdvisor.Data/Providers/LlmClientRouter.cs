using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Providers.Anthropic;
using InvestAdvisor.Data.Providers.OpenAiCompat;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// The DI-registered <see cref="ILlmClient"/>. Reads the provider + model selection from
/// <see cref="RuntimeSettings"/> on every call (so a Settings change applies without a restart)
/// and dispatches to the Anthropic client or the generic OpenAI-compatible client. API keys come
/// from <see cref="LlmOptions"/>/<see cref="AnthropicOptions"/> config, never from the DB.
/// </summary>
public sealed class LlmClientRouter(
    AnthropicClient anthropic,
    OpenAiCompatibleClient openAi,
    IRuntimeSettingsStore settingsStore,
    IOptions<LlmOptions> options) : ILlmClient
{
    private readonly LlmOptions _opts = options.Value;

    public async Task<LlmAnalysisResult> AnalyzeAsync(
        string systemPrompt, string runContextJson, string? model = null, CancellationToken ct = default)
    {
        var s = await settingsStore.GetAsync(ct);
        var resolved = Resolve(model, s.LlmModel);
        return IsAnthropic(s)
            ? await anthropic.AnalyzeAsync(systemPrompt, runContextJson, resolved, ct)
            : await openAi.AnalyzeAsync(ResolveEndpoint(s), resolved, systemPrompt, runContextJson, ct);
    }

    public async Task<StockAnalysisResult> AnalyzeStockAsync(
        string systemPrompt, string stockContextJson, string? model = null, CancellationToken ct = default)
    {
        var s = await settingsStore.GetAsync(ct);
        var resolved = Resolve(model, s.LlmModel);
        return IsAnthropic(s)
            ? await anthropic.AnalyzeStockAsync(systemPrompt, stockContextJson, resolved, ct)
            : await openAi.AnalyzeStockAsync(ResolveEndpoint(s), resolved, systemPrompt, stockContextJson, ct);
    }

    public async Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt, string candidatesContextJson, string? model = null, CancellationToken ct = default)
    {
        var s = await settingsStore.GetAsync(ct);
        var resolved = Resolve(model, s.LlmModel);
        return IsAnthropic(s)
            ? await anthropic.RecommendAllocationAsync(systemPrompt, candidatesContextJson, resolved, ct)
            : await openAi.RecommendAllocationAsync(ResolveEndpoint(s), resolved, systemPrompt, candidatesContextJson, ct);
    }

    public async Task<SentimentBatchResult> ScoreSentimentAsync(
        IReadOnlyList<string> items, string? model = null, CancellationToken ct = default)
    {
        var s = await settingsStore.GetAsync(ct);
        var resolved = Resolve(model, s.LlmRoutineModel);
        return IsAnthropic(s)
            ? await anthropic.ScoreSentimentAsync(items, resolved, ct)
            : await openAi.ScoreSentimentAsync(ResolveEndpoint(s), resolved, items, ct);
    }

    private static string Resolve(string? model, string settingsModel) =>
        string.IsNullOrWhiteSpace(model) ? settingsModel : model;

    private static bool IsAnthropic(RuntimeSettings s) =>
        string.Equals(s.LlmProvider, LlmProviders.Anthropic, StringComparison.OrdinalIgnoreCase);

    private LlmEndpoint ResolveEndpoint(RuntimeSettings s)
    {
        if (string.Equals(s.LlmProvider, LlmProviders.Custom, StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = !string.IsNullOrWhiteSpace(s.LlmCustomBaseUrl) ? s.LlmCustomBaseUrl! : _opts.CustomBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    "Custom LLM base URL not configured. Set it in Settings → AI Provider " +
                    "(e.g. https://api.groq.com/openai/v1/ or http://localhost:11434/v1/).");
            return new LlmEndpoint(baseUrl, _opts.CustomApiKey, "Custom LLM");
        }

        // Default: Gemini's OpenAI-compatible endpoint (free tier).
        if (string.IsNullOrWhiteSpace(_opts.GeminiApiKey))
            throw new InvalidOperationException(
                "Gemini API key not configured. Get a free key at https://aistudio.google.com and set " +
                "Llm:GeminiApiKey via user-secrets or the GEMINI_API_KEY env var — or switch provider " +
                "in Settings → AI Provider.");
        return new LlmEndpoint(_opts.GeminiBaseUrl, _opts.GeminiApiKey, "Gemini");
    }
}
