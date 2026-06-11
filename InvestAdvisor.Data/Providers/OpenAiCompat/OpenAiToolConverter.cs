using System.Text.Json.Nodes;

namespace InvestAdvisor.Data.Providers.OpenAiCompat;

/// <summary>
/// Rewraps the Anthropic-shaped tool schemas (<c>{name, description, input_schema}</c> from the
/// <c>Emit*ToolSchema</c> classes) into the OpenAI function-tool shape
/// (<c>{type:"function", function:{name, description, parameters}}</c>).
/// </summary>
internal static class OpenAiToolConverter
{
    public static JsonObject ToFunctionTool(JsonObject anthropicToolNode)
    {
        var parameters = anthropicToolNode["input_schema"]?.DeepClone() ?? new JsonObject();
        // Gemini's OpenAI-compat layer rejects some strict JSON-Schema keywords; the parsers are
        // lenient anyway, so stripping additionalProperties is safe and keeps every provider happy.
        StripAdditionalProperties(parameters);

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = anthropicToolNode["name"]?.DeepClone(),
                ["description"] = anthropicToolNode["description"]?.DeepClone(),
                ["parameters"] = parameters,
            },
        };
    }

    private static void StripAdditionalProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("additionalProperties");
                foreach (var kv in obj.ToList()) StripAdditionalProperties(kv.Value);
                break;
            case JsonArray arr:
                foreach (var item in arr) StripAdditionalProperties(item);
                break;
        }
    }
}
