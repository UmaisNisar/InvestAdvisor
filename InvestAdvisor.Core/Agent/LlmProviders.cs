namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Provider ids stored in <c>RuntimeSettings.LlmProvider</c> and switched in Settings → AI Provider.
/// </summary>
public static class LlmProviders
{
    /// <summary>Google Gemini via its OpenAI-compatible endpoint — free tier, the default.</summary>
    public const string Gemini = "gemini";

    /// <summary>Anthropic Claude Messages API — paid.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>Any other OpenAI-compatible endpoint (Groq, OpenRouter, Ollama, …).</summary>
    public const string Custom = "custom";
}
