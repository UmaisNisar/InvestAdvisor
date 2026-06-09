using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvestAdvisor.Data.Providers.Anthropic;

/// <summary>Top-level Anthropic Messages API response.</summary>
internal sealed class AnthropicMessageResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
    [JsonPropertyName("content")] public List<AnthropicContentBlock> Content { get; set; } = new();
    [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicContentBlock
{
    [JsonPropertyName("type")] public string? Type { get; set; }

    // Present when type == "text"
    [JsonPropertyName("text")] public string? Text { get; set; }

    // Present when type == "tool_use"
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("input")] public JsonElement Input { get; set; }
}

internal sealed class AnthropicUsage
{
    [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
}
