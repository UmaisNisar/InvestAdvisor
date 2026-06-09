namespace InvestAdvisor.Data.Providers.Anthropic;

/// <summary>
/// Static text used in the Anthropic Messages API user message envelope. Kept in one place
/// so prompt tweaks here don't drift across the codebase.
/// </summary>
internal static class AnthropicEnvelope
{
    public const string UserMessagePreamble =
        "Here is the current structured context for the portfolio as JSON. " +
        "Call the emit_analysis tool exactly once to return your structured response.\n\n";
}
