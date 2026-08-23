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

    public Task<LlmAnalysisResult> AnalyzeAsync(
        string systemPrompt, string runContextJson, string? model = null, CancellationToken ct = default) =>
        DispatchAsync(model, routine: false,
            m => anthropic.AnalyzeAsync(systemPrompt, runContextJson, m, ct),
            (e, m) => openAi.AnalyzeAsync(e, m, systemPrompt, runContextJson, ct), ct);

    public Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt, string candidatesContextJson, string? model = null, CancellationToken ct = default) =>
        DispatchAsync(model, routine: false,
            m => anthropic.RecommendAllocationAsync(systemPrompt, candidatesContextJson, m, ct),
            (e, m) => openAi.RecommendAllocationAsync(e, m, systemPrompt, candidatesContextJson, ct), ct);

    public Task<SentimentBatchResult> ScoreSentimentAsync(
        IReadOnlyList<string> items, string? model = null, CancellationToken ct = default) =>
        DispatchAsync(model, routine: true,
            m => anthropic.ScoreSentimentAsync(items, m, ct),
            (e, m) => openAi.ScoreSentimentAsync(e, m, items, ct), ct);

    /// <summary>
    /// Reads the provider + model from settings (the routine model for cheap batch calls) and
    /// dispatches to whichever concrete client is selected.
    /// </summary>
    private async Task<T> DispatchAsync<T>(
        string? model, bool routine,
        Func<string, Task<T>> viaAnthropic,
        Func<LlmEndpoint, string, Task<T>> viaOpenAi,
        CancellationToken ct)
    {
        var s = await settingsStore.GetAsync(ct);
        var settingsModel = routine ? s.LlmRoutineModel : s.LlmModel;
        var resolved = string.IsNullOrWhiteSpace(model) ? settingsModel : model;
        return string.Equals(s.LlmProvider, LlmProviders.Anthropic, StringComparison.OrdinalIgnoreCase)
            ? await viaAnthropic(resolved)
            : await viaOpenAi(ResolveEndpoint(s), resolved);
    }

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
