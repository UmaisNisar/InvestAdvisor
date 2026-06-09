namespace InvestAdvisor.Core.Options;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-sonnet-4-6";
    // Needs headroom: the forced tool output now includes a per-holding positions array, which
    // overflowed the old 2048 cap (response truncated at max_tokens before positions were emitted).
    public int MaxTokens { get; set; } = 4096;
    public string ApiVersion { get; set; } = "2023-06-01";
    public int TimeoutSeconds { get; set; } = 90;
}
