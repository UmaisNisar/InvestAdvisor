using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvestAdvisor.Core.Agent;

internal static class JsonOptions
{
    /// <summary>Serializer options used when emitting <c>RunContext</c> to the LLM.</summary>
    public static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Same shape, indented — used for the captured <c>StructuredInputJson</c> in <c>AdviceLog</c> for readability.</summary>
    public static readonly JsonSerializerOptions CamelIndented = new(Camel) { WriteIndented = true };
}
