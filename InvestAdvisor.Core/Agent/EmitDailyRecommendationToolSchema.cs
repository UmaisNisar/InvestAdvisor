using System.Text.Json.Nodes;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// Output contract for the single daily "where to invest" call. The model is forced to invoke this
/// tool exactly once, returning a focused shortlist per asset class chosen ONLY from the supplied
/// candidates. Picks are research leads, not advice.
/// </summary>
public static class EmitDailyRecommendationToolSchema
{
    public const string ToolName = "emit_daily_recommendation";

    public const string SchemaJson = """
    {
      "name": "emit_daily_recommendation",
      "description": "Emit today's focused buy shortlist per asset class, chosen ONLY from the supplied candidates. Be selective; an empty list for a class is valid when nothing is compelling. Reasons must be grounded in the provided data. Not financial advice.",
      "input_schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["summary", "stocks", "etfs", "crypto", "caution"],
        "properties": {
          "summary": {
            "type": "string",
            "description": "2-4 sentence overall read of today's setup and how aggressive or cautious to be, tailored to the investor's profile."
          },
          "stocks": {
            "type": "array",
            "description": "Stocks to consider buying today, chosen only from the supplied stock candidates. 0-4 items.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["ticker", "reason"],
              "properties": {
                "ticker": { "type": "string", "description": "Ticker, exactly as supplied in the candidates." },
                "reason": { "type": "string", "description": "One sentence, grounded in the supplied data." }
              }
            }
          },
          "etfs": {
            "type": "array",
            "description": "ETFs to consider buying today, chosen only from the supplied ETF candidates. 0-4 items.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["ticker", "reason"],
              "properties": {
                "ticker": { "type": "string" },
                "reason": { "type": "string" }
              }
            }
          },
          "crypto": {
            "type": "array",
            "description": "Crypto to consider buying today, chosen only from the supplied crypto candidates. 0-3 items; be especially cautious.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["ticker", "reason"],
              "properties": {
                "ticker": { "type": "string" },
                "reason": { "type": "string" }
              }
            }
          },
          "caution": {
            "type": "string",
            "description": "Key risks and the plain reminder that this is research synthesis, not advice."
          }
        }
      }
    }
    """;

    private static readonly Lazy<JsonObject> _toolObject = new(() =>
        (JsonNode.Parse(SchemaJson) as JsonObject)
            ?? throw new InvalidOperationException("emit_daily_recommendation schema failed to parse"));

    public static JsonObject AsToolNode() => (JsonObject)_toolObject.Value.DeepClone();
}
