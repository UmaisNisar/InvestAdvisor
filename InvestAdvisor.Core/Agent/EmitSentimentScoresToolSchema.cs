using System.Text.Json.Nodes;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Output contract for batch sentiment scoring. The model reads a numbered list of news/social
/// items and is forced to invoke this tool exactly once, returning one score per item keyed by its
/// index. Score is the investor-relevant sentiment toward the named ticker, NOT a price prediction.
/// </summary>
public static class EmitSentimentScoresToolSchema
{
    public const string ToolName = "emit_sentiment_scores";

    public const string SchemaJson = """
    {
      "name": "emit_sentiment_scores",
      "description": "Emit an investor-sentiment score for each numbered item. Judge the tone toward the security/asset the item concerns: positive/bullish vs negative/bearish, grounded ONLY in the item text. This is a read of tone, not a price prediction or trade recommendation. Return exactly one entry per input index.",
      "input_schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["scores"],
        "properties": {
          "scores": {
            "type": "array",
            "description": "One entry per input item, in any order, each referencing its input index.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["index", "score", "label"],
              "properties": {
                "index": {
                  "type": "integer",
                  "minimum": 0,
                  "description": "Zero-based index of the item being scored, matching the input list."
                },
                "score": {
                  "type": "number",
                  "minimum": -1,
                  "maximum": 1,
                  "description": "Sentiment in [-1, 1]: -1 strongly bearish, 0 neutral/factual, +1 strongly bullish."
                },
                "label": {
                  "type": "string",
                  "enum": ["bearish", "neutral", "bullish"],
                  "description": "Bucketed sentiment consistent with the score sign."
                }
              }
            }
          }
        }
      }
    }
    """;

    private static readonly Lazy<JsonObject> _toolObject = new(() =>
        (JsonNode.Parse(SchemaJson) as JsonObject)
            ?? throw new InvalidOperationException("emit_sentiment_scores schema failed to parse"));

    public static JsonObject AsToolNode() => (JsonObject)_toolObject.Value.DeepClone();
}
