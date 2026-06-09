using System.Text.Json.Nodes;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Output contract for the screener's per-stock LLM analysis. The model is forced to invoke
/// this tool exactly once so the response shape is deterministic. Conviction is a qualitative
/// read of how well the data supports the thesis — not a price prediction or a trade recommendation.
/// </summary>
public static class EmitStockAnalysisToolSchema
{
    public const string ToolName = "emit_stock_analysis";

    public const string SchemaJson = """
    {
      "name": "emit_stock_analysis",
      "description": "Emit a structured single-stock analysis grounded ONLY in the provided data. Build a balanced bull and bear case. Do not predict prices or guarantee outcomes. Conviction reflects how well the data supports the thesis, not certainty.",
      "input_schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["summary", "thesis", "bullishFactors", "bearishFactors", "keyRisks", "conviction", "convictionLabel"],
        "properties": {
          "summary": {
            "type": "string",
            "description": "1-3 sentence plain-language read of where this stock stands, grounded in the data."
          },
          "thesis": {
            "type": "string",
            "description": "2-4 sentence balanced investment thesis — the core argument, acknowledging both sides."
          },
          "bullishFactors": {
            "type": "array",
            "items": { "type": "string" },
            "description": "Concrete positives drawn from the data. 2-5 short items."
          },
          "bearishFactors": {
            "type": "array",
            "items": { "type": "string" },
            "description": "Concrete negatives or concerns drawn from the data. 2-5 short items."
          },
          "keyRisks": {
            "type": "array",
            "items": { "type": "string" },
            "description": "Specific risks to watch. 1-4 short items."
          },
          "conviction": {
            "type": "integer",
            "minimum": 0,
            "maximum": 100,
            "description": "Qualitative conviction 0-100 (50 = neutral). NOT a price target or guarantee. Reflect uncertainty honestly."
          },
          "convictionLabel": {
            "type": "string",
            "enum": ["low", "medium", "high"],
            "description": "Bucketed conviction."
          }
        }
      }
    }
    """;

    private static readonly Lazy<JsonObject> _toolObject = new(() =>
        (JsonNode.Parse(SchemaJson) as JsonObject)
            ?? throw new InvalidOperationException("emit_stock_analysis schema failed to parse"));

    public static JsonObject AsToolNode() => (JsonObject)_toolObject.Value.DeepClone();
}
