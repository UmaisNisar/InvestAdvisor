using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Providers.Anthropic;

/// <summary>
/// Typed HttpClient against the Anthropic Messages API. Forces <c>tool_choice</c> to
/// <c>emit_analysis</c> on every request so the response shape is deterministic; falls
/// back to extracting the first balanced JSON object from text content when the model
/// fails to return a <c>tool_use</c> block.
/// </summary>
public sealed class AnthropicClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicClient>? logger = null) : IAnthropicClient
{
    private readonly AnthropicOptions _opts = options.Value;

    private static readonly JsonSerializerOptions _toolInputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<AnthropicAnalysisResult> AnalyzeAsync(
        string systemPrompt,
        string runContextJson,
        CancellationToken ct = default)
    {
        var body = BuildRequestBody(systemPrompt, runContextJson);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (analysis, fallbackUsed) = ParseAnalysis(parsed, rawBody);

        return new AnthropicAnalysisResult(
            Analysis: analysis,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? _opts.Model,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<StockAnalysisResult> AnalyzeStockAsync(
        string systemPrompt,
        string stockContextJson,
        CancellationToken ct = default)
    {
        var body = BuildStockRequestBody(systemPrompt, stockContextJson);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (s, fallbackUsed) = ParseStockAnalysis(parsed, rawBody);

        return new StockAnalysisResult(
            Summary: s.Summary,
            Thesis: s.Thesis,
            BullishFactors: s.Bullish,
            BearishFactors: s.Bearish,
            KeyRisks: s.Risks,
            Conviction: s.Conviction,
            ConvictionLabel: s.ConvictionLabel,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? _opts.Model,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    public async Task<DailyRecommendationResult> RecommendAllocationAsync(
        string systemPrompt,
        string candidatesContextJson,
        CancellationToken ct = default)
    {
        var body = BuildRecommendationRequestBody(systemPrompt, candidatesContextJson);
        var (parsed, rawBody, latencyMs) = await SendAsync(body, ct);
        var (rec, fallbackUsed) = ParseRecommendation(parsed, rawBody);

        return new DailyRecommendationResult(
            Summary: rec.Summary,
            Caution: rec.Caution,
            Stocks: rec.Stocks,
            Etfs: rec.Etfs,
            Crypto: rec.Crypto,
            RawResponseBody: rawBody,
            Model: parsed.Model ?? _opts.Model,
            InputTokens: parsed.Usage?.InputTokens ?? 0,
            OutputTokens: parsed.Usage?.OutputTokens ?? 0,
            LatencyMs: latencyMs,
            ParseFallbackUsed: fallbackUsed);
    }

    private async Task<(AnthropicMessageResponse Parsed, string RawBody, int LatencyMs)> SendAsync(
        JsonObject body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "Anthropic API key not configured. Set Anthropic:ApiKey via user-secrets or the ANTHROPIC_API_KEY env var.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { WriteIndented = false }),
        };

        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogError("Anthropic API call failed: {Status} {Body}", response.StatusCode, rawBody);
            throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {rawBody}");
        }

        var parsed = JsonSerializer.Deserialize<AnthropicMessageResponse>(rawBody)
            ?? throw new AgentParseException("Anthropic response body deserialized to null.", rawBody);

        return (parsed, rawBody, (int)sw.ElapsedMilliseconds);
    }

    private JsonObject BuildRequestBody(string systemPrompt, string runContextJson)
    {
        var body = new JsonObject
        {
            ["model"] = _opts.Model,
            ["max_tokens"] = _opts.MaxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = AnthropicEnvelope.UserMessagePreamble + runContextJson,
                },
            },
            ["tools"] = new JsonArray { EmitAnalysisToolSchema.AsToolNode() },
            ["tool_choice"] = new JsonObject
            {
                ["type"] = "tool",
                ["name"] = EmitAnalysisToolSchema.ToolName,
            },
        };
        return body;
    }

    private JsonObject BuildStockRequestBody(string systemPrompt, string stockContextJson) => new()
    {
        ["model"] = _opts.Model,
        ["max_tokens"] = _opts.MaxTokens,
        ["system"] = systemPrompt,
        ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Analyze this single stock from the structured data below. " +
                              "Call the emit_stock_analysis tool exactly once.\n\n" + stockContextJson,
            },
        },
        ["tools"] = new JsonArray { EmitStockAnalysisToolSchema.AsToolNode() },
        ["tool_choice"] = new JsonObject
        {
            ["type"] = "tool",
            ["name"] = EmitStockAnalysisToolSchema.ToolName,
        },
    };

    private readonly record struct StockFields(
        string Summary, string Thesis,
        IReadOnlyList<string> Bullish, IReadOnlyList<string> Bearish, IReadOnlyList<string> Risks,
        int Conviction, string ConvictionLabel);

    private static (StockFields Fields, bool FallbackUsed) ParseStockAnalysis(
        AnthropicMessageResponse response, string rawBody)
    {
        var toolUse = response.Content.FirstOrDefault(c =>
            string.Equals(c.Type, "tool_use", StringComparison.Ordinal) &&
            string.Equals(c.Name, EmitStockAnalysisToolSchema.ToolName, StringComparison.Ordinal));

        if (toolUse is not null && toolUse.Input.ValueKind == JsonValueKind.Object)
        {
            try { return (DeserializeStock(toolUse.Input), false); }
            catch (Exception ex)
            {
                throw new AgentParseException(
                    "tool_use payload did not match emit_stock_analysis schema.", rawBody, ex);
            }
        }

        var textConcat = string.Join('\n',
            response.Content.Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text)).Select(c => c.Text));
        var extracted = ExtractFirstJsonObject(textConcat);
        if (extracted is null)
            throw new AgentParseException(
                "Stock analysis response had no tool_use block and no parseable JSON in text.", rawBody);

        try
        {
            using var doc = JsonDocument.Parse(extracted);
            return (DeserializeStock(doc.RootElement), true);
        }
        catch (JsonException ex)
        {
            throw new AgentParseException("Fallback JSON extraction failed for stock analysis.", rawBody, ex);
        }
    }

    private static StockFields DeserializeStock(JsonElement input)
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

    private JsonObject BuildRecommendationRequestBody(string systemPrompt, string candidatesContextJson) => new()
    {
        ["model"] = _opts.Model,
        ["max_tokens"] = _opts.MaxTokens,
        ["system"] = systemPrompt,
        ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "From the ranked candidates below, choose where to invest today in each " +
                              "asset class. Call the emit_daily_recommendation tool exactly once.\n\n" +
                              candidatesContextJson,
            },
        },
        ["tools"] = new JsonArray { EmitDailyRecommendationToolSchema.AsToolNode() },
        ["tool_choice"] = new JsonObject
        {
            ["type"] = "tool",
            ["name"] = EmitDailyRecommendationToolSchema.ToolName,
        },
    };

    private readonly record struct RecFields(
        string Summary, string Caution,
        IReadOnlyList<RecommendedPick> Stocks, IReadOnlyList<RecommendedPick> Etfs, IReadOnlyList<RecommendedPick> Crypto);

    private static (RecFields Fields, bool FallbackUsed) ParseRecommendation(
        AnthropicMessageResponse response, string rawBody)
    {
        var toolUse = response.Content.FirstOrDefault(c =>
            string.Equals(c.Type, "tool_use", StringComparison.Ordinal) &&
            string.Equals(c.Name, EmitDailyRecommendationToolSchema.ToolName, StringComparison.Ordinal));

        if (toolUse is not null && toolUse.Input.ValueKind == JsonValueKind.Object)
        {
            try { return (DeserializeRecommendation(toolUse.Input), false); }
            catch (Exception ex)
            {
                throw new AgentParseException(
                    "tool_use payload did not match emit_daily_recommendation schema.", rawBody, ex);
            }
        }

        var textConcat = string.Join('\n',
            response.Content.Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text)).Select(c => c.Text));
        var extracted = ExtractFirstJsonObject(textConcat);
        if (extracted is null)
            throw new AgentParseException(
                "Daily recommendation response had no tool_use block and no parseable JSON in text.", rawBody);

        try
        {
            using var doc = JsonDocument.Parse(extracted);
            return (DeserializeRecommendation(doc.RootElement), true);
        }
        catch (JsonException ex)
        {
            throw new AgentParseException("Fallback JSON extraction failed for daily recommendation.", rawBody, ex);
        }
    }

    private static RecFields DeserializeRecommendation(JsonElement input)
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

    private static (AgentAnalysis Analysis, bool FallbackUsed) ParseAnalysis(
        AnthropicMessageResponse response,
        string rawBody)
    {
        // Happy path: a tool_use block named emit_analysis.
        var toolUse = response.Content.FirstOrDefault(c =>
            string.Equals(c.Type, "tool_use", StringComparison.Ordinal) &&
            string.Equals(c.Name, EmitAnalysisToolSchema.ToolName, StringComparison.Ordinal));

        if (toolUse is not null && toolUse.Input.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var analysis = DeserializeAnalysis(toolUse.Input, model: response.Model ?? string.Empty,
                    response: response, fallback: false);
                return (analysis, false);
            }
            catch (Exception ex)
            {
                throw new AgentParseException(
                    "tool_use payload did not match emit_analysis schema.", rawBody, ex);
            }
        }

        // Fallback: try to extract the first balanced JSON object from any text content block.
        var textConcat = string.Join('\n',
            response.Content.Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text))
                            .Select(c => c.Text));

        var extracted = ExtractFirstJsonObject(textConcat);
        if (extracted is null)
            throw new AgentParseException(
                "Anthropic response had no tool_use block and no parseable JSON in text content.",
                rawBody);

        try
        {
            using var doc = JsonDocument.Parse(extracted);
            var analysis = DeserializeAnalysis(doc.RootElement, model: response.Model ?? string.Empty,
                response: response, fallback: true);
            return (analysis, true);
        }
        catch (JsonException ex)
        {
            throw new AgentParseException("Fallback JSON extraction failed to deserialize.", rawBody, ex);
        }
    }

    private static AgentAnalysis DeserializeAnalysis(
        JsonElement input,
        string model,
        AnthropicMessageResponse response,
        bool fallback)
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
                InputTokens: response.Usage?.InputTokens ?? 0,
                OutputTokens: response.Usage?.OutputTokens ?? 0,
                LatencyMs: 0,
                ParseFallbackUsed: fallback),
            Positions: positions);
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
