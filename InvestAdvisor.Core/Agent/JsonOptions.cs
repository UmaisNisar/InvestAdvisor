using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// The three <see cref="JsonSerializerOptions"/> shapes the app uses, in one place so LLM
/// context, persisted parsed columns, and their readers can never drift apart.
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// Compact camel-case, nulls omitted — for JSON sent to the LLM, where indentation would
    /// just be billed whitespace.
    /// </summary>
    public static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Same shape, indented — for the parsed-output columns on <c>AdviceLog</c>, read by humans in the UI.</summary>
    public static readonly JsonSerializerOptions CamelIndented = new(Camel) { WriteIndented = true };

    /// <summary>
    /// Reader for the parsed-output columns: camel-case with enums accepted as either their
    /// integer value or camel-case name, so rows written before and after any enum-format change
    /// both rehydrate.
    /// </summary>
    public static readonly JsonSerializerOptions CamelEnums = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Deserializes a persisted JSON array column, treating blank or malformed JSON as empty.</summary>
    public static IReadOnlyList<T> ArrayOrEmpty<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<T>();
        try { return JsonSerializer.Deserialize<T[]>(json, CamelEnums) ?? Array.Empty<T>(); }
        catch (JsonException) { return Array.Empty<T>(); }
    }
}
