namespace InvestAdvisor.Core.Options;

/// <summary>
/// Config-only LLM provider settings (API keys + endpoints). Which provider/model is active is a
/// runtime choice stored in <c>RuntimeSettings</c> and edited in Settings → AI Provider; the keys
/// themselves stay in user-secrets/env and are never persisted to SQLite.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Google AI Studio API key (free tier) for the Gemini OpenAI-compatible endpoint.
    /// Get one at https://aistudio.google.com — no billing required.
    /// </summary>
    public string GeminiApiKey { get; set; } = string.Empty;

    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    /// <summary>
    /// API key for the "custom" provider (Groq, OpenRouter, or any other OpenAI-compatible API).
    /// Blank is fine for endpoints without auth, e.g. a local Ollama.
    /// </summary>
    public string CustomApiKey { get; set; } = string.Empty;

    /// <summary>Fallback base URL for the custom provider; the Settings-page value wins when set.</summary>
    public string CustomBaseUrl { get; set; } = string.Empty;

    // Generous: Gemini 2.5 models spend "thinking" tokens that count toward the completion
    // budget, so the old 4096 cap could truncate the forced tool call before it was emitted.
    public int MaxTokens { get; set; } = 8192;

    public int TimeoutSeconds { get; set; } = 90;
}
