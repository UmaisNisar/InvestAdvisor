using System.Text.Json;
using System.Text.Json.Nodes;

namespace InvestAdvisor.Core.Agent;

/// <summary>
/// The authoritative output contract sent to Anthropic on every run. The LLM is forced
/// to invoke this tool exactly once (via <c>tool_choice</c>) so the response shape is
/// deterministic.
/// </summary>
public static class EmitAnalysisToolSchema
{
    public const string ToolName = "emit_analysis";

    public const string SchemaJson = """
    {
      "name": "emit_analysis",
      "description": "Emit the structured analysis of the investor's portfolio state. Use this tool exactly once. Do not predict prices. Keep 'summary', 'flags', and 'considerations' observational and neutral. The 'positions' array is the exception: it gives a per-holding stance (add/hold/trim/sell) as decision support for this single user (their own tool), grounded only in the supplied data and the user's stated rules.",
      "input_schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["summary", "flags", "driftAlerts", "considerations", "positions"],
        "properties": {
          "summary": {
            "type": "string",
            "description": "2-5 sentence plain-language summary of what changed since the last run and what is most worth the user's attention. No predictions. No recommendations to trade."
          },
          "flags": {
            "type": "array",
            "description": "Discrete items worth the user's attention. Each flag is one observation about one ticker or the market.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["severity", "ticker", "title", "detail"],
              "properties": {
                "severity": {
                  "type": "string",
                  "enum": ["info", "warn", "critical"],
                  "description": "info = noteworthy but routine; warn = user should look at this today; critical = user should look at this now."
                },
                "ticker": {
                  "type": ["string", "null"],
                  "description": "Ticker the flag concerns, or null for market-wide flags."
                },
                "title": { "type": "string", "description": "Short headline, under 80 chars." },
                "detail": { "type": "string", "description": "1-3 sentence explanation grounded in the provided data." },
                "evidence": {
                  "type": "array",
                  "description": "Optional pointers back to the input data.",
                  "items": { "type": "string" }
                }
              }
            }
          },
          "driftAlerts": {
            "type": "array",
            "description": "Holdings whose current allocation has drifted from the user's stated target beyond their stated threshold.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["severity", "ticker", "currentPct", "targetPct", "driftPct", "note"],
              "properties": {
                "severity": {
                  "type": "string",
                  "enum": ["note", "action_suggested"],
                  "description": "note = inside threshold but trending; action_suggested = exceeds the user's own threshold."
                },
                "ticker": { "type": "string" },
                "currentPct": { "type": "number" },
                "targetPct": { "type": "number" },
                "driftPct": { "type": "number", "description": "Signed: current - target." },
                "note": { "type": "string", "description": "Neutral framing of the drift. Do not tell the user to buy or sell." }
              }
            }
          },
          "considerations": {
            "type": "array",
            "description": "Neutral trade-offs and discussion points. Bullet-style, no recommendations.",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["topic", "text"],
              "properties": {
                "topic": {
                  "type": "string",
                  "description": "Short tag, e.g. 'tax', 'concentration', 'macro', 'discipline', 'liquidity'."
                },
                "text": { "type": "string", "description": "One paragraph, neutral framing." }
              }
            }
          },
          "positions": {
            "type": "array",
            "description": "A stance for EVERY holding currently in the portfolio — exactly one entry per holding, no extras and none omitted. Decision support for the single user who owns this tool; the human decides. Pick the single most reasonable action from the supplied data and the user's stated goals/risk tolerance. Be decisive: do NOT default to 'hold' unless the evidence genuinely supports holding. Ground each call in concrete numbers (P/L vs cost, today's move, allocation vs the user's target, concentration, momentum, news).",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["ticker", "stance", "conviction", "reason"],
              "properties": {
                "ticker": { "type": "string", "description": "Exact ticker as provided in the portfolio data." },
                "stance": {
                  "type": "string",
                  "enum": ["add", "hold", "trim", "sell"],
                  "description": "add = consider buying more; hold = keep as-is; trim = consider reducing the position (e.g. concentration or rebalancing); sell = consider exiting fully."
                },
                "conviction": {
                  "type": "string",
                  "enum": ["low", "medium", "high"],
                  "description": "How strongly the supplied data supports this stance. high only when the evidence is clear and specific."
                },
                "reason": { "type": "string", "description": "One specific sentence justifying the stance, citing concrete numbers from the data. No price predictions." }
              }
            }
          }
        }
      }
    }
    """;

    private static readonly Lazy<JsonObject> _toolObject = new(() =>
        (JsonNode.Parse(SchemaJson) as JsonObject)
            ?? throw new InvalidOperationException("emit_analysis schema failed to parse"));

    /// <summary>Returns the tool as a JsonObject suitable for inclusion in the Anthropic request body.</summary>
    public static JsonObject AsToolNode() => (JsonObject)_toolObject.Value.DeepClone();
}
