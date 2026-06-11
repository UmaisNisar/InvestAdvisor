using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Data.Providers;

/// <summary>
/// Provider-neutral parsing for the structured LLM payloads. Each client extracts the forced
/// tool-call input (and any plain-text content) from its own wire format; everything from there —
/// schema-tolerant deserialization plus the balanced-JSON text fallback — is shared here so the
/// Anthropic and OpenAI-compatible clients behave identically.
/// </summary>
internal static class LlmResponseParsing
{
    internal readonly record struct StockFields(
        string Summary, string Thesis,
        IReadOnlyList<string> Bullish, IReadOnlyList<string> Bearish, IReadOnlyList<string> Risks,
        int Conviction, string ConvictionLabel);

    internal readonly record struct RecFields(
        string Summary, string Caution,
        IReadOnlyList<RecommendedPick> Stocks, IReadOnlyList<RecommendedPick> Etfs, IReadOnlyList<RecommendedPick> Crypto);

    /// <summary>
    /// Resolves the structured payload: prefers the forced tool-call input; otherwise extracts the
    /// first balanced JSON object from the text content and flags the fallback.
    /// </summary>
    internal static (T Value, bool FallbackUsed) Parse<T>(
        JsonElement? toolInput,
        string textConcat,
        string rawBody,
        string toolName,
        Func<JsonElement, T> deserialize)
    {
        if (toolInput is { ValueKind: JsonValueKind.Object } input)
        {
            try { return (deserialize(input), false); }
            catch (Exception ex)
            {
                throw new AgentParseException(
                    $"Tool-call payload did not match the {toolName} schema.", rawBody, ex);
            }
        }

        var extracted = ExtractFirstJsonObject(textConcat);
        if (extracted is null)
            throw new AgentParseException(
                $"{toolName} response had no tool call and no parseable JSON in text content.", rawBody);

        try
        {
            using var doc = JsonDocument.Parse(extracted);
            return (deserialize(doc.RootElement), true);
        }
        catch (JsonException ex)
        {
            throw new AgentParseException($"Fallback JSON extraction failed for {toolName}.", rawBody, ex);
        }
    }

    internal static AgentAnalysis DeserializeAnalysis(
        JsonElement input, string model, int inputTokens, int outputTokens)
    {
        var summary = input.TryGetProperty("summary", out var sEl) ? (sEl.GetString() ?? string.Empty) : string.Empty;

        var flags = input.TryGetProperty("flags", out var fEl) && fEl.ValueKind == JsonValueKind.Array
            ? ParseFlags(fEl)
            : Array.Empty<Flag>();

        var drift = input.TryGetProperty("driftAlerts", out var dEl) && dEl.ValueKind == JsonValueKind.Array
            ? ParseDriftAlerts(dEl)
            : Array.Empty<DriftAlert>();

        var cons = input.TryGetProperty("considerations", out var cEl) && cEl.ValueKind == JsonValueKind.Array
            ? ParseConsiderations(cEl)
            : Array.Empty<Consideration>();

        var positions = input.TryGetProperty("positions", out var pEl) && pEl.ValueKind == JsonValueKind.Array
            ? ParsePositions(pEl)
            : Array.Empty<PositionCall>();

        return new AgentAnalysis(
            Summary: summary,
            Flags: flags,
            DriftAlerts: drift,
            Considerations: cons,
            Metrics: new AgentRunMetrics(
                Model: model,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                LatencyMs: 0,
                ParseFallbackUsed: false),
            Positions: positions);
    }

    internal static StockFields DeserializeStock(JsonElement input)
    {
        string Str(string name) =>
            input.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";

        IReadOnlyList<string> Arr(string name)
        {
            if (!input.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var list = new List<string>(e.GetArrayLength());
            foreach (var item in e.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                }
            return list;
        }

        var conviction = 50;
        if (input.TryGetProperty("conviction", out var cv))
        {
            if (cv.ValueKind == JsonValueKind.Number && cv.TryGetInt32(out var ci)) conviction = ci;
            else if (cv.ValueKind == JsonValueKind.String && int.TryParse(cv.GetString(), out var cs)) conviction = cs;
        }
        conviction = Math.Clamp(conviction, 0, 100);

        var label = Str("convictionLabel").ToLowerInvariant();
        if (label is not ("low" or "medium" or "high"))
            label = conviction >= 67 ? "high" : conviction >= 34 ? "medium" : "low";

        return new StockFields(
            Str("summary"), Str("thesis"),
            Arr("bullishFactors"), Arr("bearishFactors"), Arr("keyRisks"),
            conviction, label);
    }

    internal static RecFields DeserializeRecommendation(JsonElement input)
    {
        string Str(string name) =>
            input.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";

        IReadOnlyList<RecommendedPick> Picks(string name)
        {
            if (!input.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Array)
                return Array.Empty<RecommendedPick>();
            var list = new List<RecommendedPick>(e.GetArrayLength());
            foreach (var item in e.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var ticker = item.TryGetProperty("ticker", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "") : "";
                var reason = item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? (r.GetString() ?? "") : "";
                if (!string.IsNullOrWhiteSpace(ticker)) list.Add(new RecommendedPick(ticker.Trim().ToUpperInvariant(), reason));
            }
            return list;
        }

        return new RecFields(Str("summary"), Str("caution"), Picks("stocks"), Picks("etfs"), Picks("crypto"));
    }

    internal static IReadOnlyList<SentimentScore> DeserializeSentiment(JsonElement input)
    {
        if (!input.TryGetProperty("scores", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<SentimentScore>();

        var list = new List<SentimentScore>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("index", out var idxEl) ||
                idxEl.ValueKind != JsonValueKind.Number || !idxEl.TryGetInt32(out var index))
                continue;

            decimal score = 0m;
            if (item.TryGetProperty("score", out var scEl))
            {
                if (scEl.ValueKind == JsonValueKind.Number && scEl.TryGetDecimal(out var sd)) score = sd;
                else if (scEl.ValueKind == JsonValueKind.String && decimal.TryParse(scEl.GetString(), out var ss)) score = ss;
            }
            score = Math.Clamp(score, -1m, 1m);

            var label = (item.TryGetProperty("label", out var lEl) && lEl.ValueKind == JsonValueKind.String
                ? lEl.GetString() : null)?.Trim().ToLowerInvariant();
            if (label is not ("bullish" or "bearish" or "neutral"))
                label = score > 0.15m ? "bullish" : score < -0.15m ? "bearish" : "neutral";

            list.Add(new SentimentScore(index, score, label));
        }
        return list;
    }

    private static IReadOnlyList<PositionCall> ParsePositions(JsonElement arr)
    {
        var list = new List<PositionCall>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var ticker = item.TryGetProperty("ticker", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(ticker)) continue;
            var stanceStr = item.TryGetProperty("stance", out var s) ? s.GetString() : null;
            var stance = stanceStr?.Trim().ToLowerInvariant() switch
            {
                "add" => PositionStance.Add,
                "trim" => PositionStance.Trim,
                "sell" => PositionStance.Sell,
                _ => PositionStance.Hold,
            };
            var convStr = item.TryGetProperty("conviction", out var cv) ? cv.GetString() : null;
            var conviction = convStr?.Trim().ToLowerInvariant() switch
            {
                "high" => PositionConviction.High,
                "low" => PositionConviction.Low,
                _ => PositionConviction.Medium,
            };
            var reason = item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? (r.GetString() ?? "") : "";
            list.Add(new PositionCall(ticker.Trim().ToUpperInvariant(), stance, conviction, reason));
        }
        return list;
    }

    private static IReadOnlyList<Flag> ParseFlags(JsonElement arr)
    {
        var list = new List<Flag>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var severityStr = item.TryGetProperty("severity", out var sev) ? sev.GetString() : null;
            var severity = ParseFlagSeverity(severityStr);
            string? ticker = item.TryGetProperty("ticker", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            var title = item.TryGetProperty("title", out var tt) ? (tt.GetString() ?? "") : "";
            var detail = item.TryGetProperty("detail", out var d) ? (d.GetString() ?? "") : "";
            List<string>? evidence = null;
            if (item.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array)
            {
                evidence = new List<string>(ev.GetArrayLength());
                foreach (var e in ev.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) evidence.Add(e.GetString() ?? "");
            }
            list.Add(new Flag(severity, ticker, title, detail, evidence));
        }
        return list;
    }

    private static IReadOnlyList<DriftAlert> ParseDriftAlerts(JsonElement arr)
    {
        var list = new List<DriftAlert>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var severityStr = item.TryGetProperty("severity", out var sev) ? sev.GetString() : null;
            var severity = ParseDriftSeverity(severityStr);
            var ticker = item.TryGetProperty("ticker", out var t) ? (t.GetString() ?? "") : "";
            var current = item.TryGetProperty("currentPct", out var c) ? c.GetDecimal() : 0m;
            var target = item.TryGetProperty("targetPct", out var tgt) ? tgt.GetDecimal() : 0m;
            var drift = item.TryGetProperty("driftPct", out var dr) ? dr.GetDecimal() : 0m;
            var note = item.TryGetProperty("note", out var n) ? (n.GetString() ?? "") : "";
            list.Add(new DriftAlert(severity, ticker, current, target, drift, note));
        }
        return list;
    }

    private static IReadOnlyList<Consideration> ParseConsiderations(JsonElement arr)
    {
        var list = new List<Consideration>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var topic = item.TryGetProperty("topic", out var t) ? (t.GetString() ?? "") : "";
            var text = item.TryGetProperty("text", out var tx) ? (tx.GetString() ?? "") : "";
            list.Add(new Consideration(topic, text));
        }
        return list;
    }

    private static FlagSeverity ParseFlagSeverity(string? s) => s?.ToLowerInvariant() switch
    {
        "critical" => FlagSeverity.Critical,
        "warn" => FlagSeverity.Warn,
        _ => FlagSeverity.Info,
    };

    private static DriftSeverity ParseDriftSeverity(string? s) =>
        string.Equals(s, "action_suggested", StringComparison.OrdinalIgnoreCase)
            ? DriftSeverity.ActionSuggested
            : DriftSeverity.Note;

    /// <summary>
    /// Extracts the first balanced JSON object from arbitrary text content. Tracks string
    /// and escape state from the start of the text so that a <c>{</c> that appears inside
    /// a surrounding string literal is correctly skipped.
    /// </summary>
    internal static string? ExtractFirstJsonObject(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    if (start < 0) start = i;
                    depth++;
                    break;
                case '}':
                    if (start < 0) continue;
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                    if (depth < 0) { start = -1; depth = 0; }
                    break;
            }
        }
        return null;
    }
}
