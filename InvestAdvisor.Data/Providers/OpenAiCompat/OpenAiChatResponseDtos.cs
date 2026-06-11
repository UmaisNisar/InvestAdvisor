using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvestAdvisor.Data.Providers.OpenAiCompat;

/// <summary>Top-level OpenAI-style chat-completions response (Gemini compat / Groq / OpenRouter / Ollama).</summary>
internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("choices")] public List<OpenAiChoice> Choices { get; set; } = new();
    [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    [JsonPropertyName("message")] public OpenAiChatMessage? Message { get; set; }
}

internal sealed class OpenAiChatMessage
{
    // string for most providers, but some return an array of content parts — keep raw and unwrap.
    [JsonPropertyName("content")] public JsonElement Content { get; set; }
    [JsonPropertyName("tool_calls")] public List<OpenAiToolCall>? ToolCalls { get; set; }

    public string ContentText => Content.ValueKind switch
    {
        JsonValueKind.String => Content.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join('\n', Content.EnumerateArray()
            .Where(p => p.ValueKind == JsonValueKind.Object &&
                        p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            .Select(p => p.GetProperty("text").GetString())),
        _ => string.Empty,
    };
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("function")] public OpenAiFunctionCall? Function { get; set; }
}

internal sealed class OpenAiFunctionCall
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>JSON-encoded arguments string (per the OpenAI wire format — not an object).</summary>
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
}
