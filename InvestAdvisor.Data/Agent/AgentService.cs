using System.Diagnostics;
using System.Text.Json;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Agent;

/// <summary>
/// Orchestrates one agent run: load profile + known tickers, assemble the context,
/// call Anthropic, validate hallucinated tickers, persist an <see cref="AdviceLog"/>,
/// and return its id. Failures still persist a row so they show up in the Advice Feed.
/// </summary>
public sealed class AgentService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    IContextAssembler assembler,
    IAnthropicClient anthropic,
    IPriceRefreshService priceRefresh,
    INewsRefreshService newsRefresh,
    ISystemClock clock,
    ILogger<AgentService>? logger = null) : IAgentService
{
    private static readonly JsonSerializerOptions _camelIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public Task<long> RunNowAsync(int tenantId, string? note, CancellationToken ct = default)
        => RunAsync(tenantId, new RunTrigger(RunTriggerKind.Manual, note ?? "Manual run"), ct);

    public async Task<long> RunAsync(int tenantId, RunTrigger trigger, CancellationToken ct = default)
    {
        var startedAt = clock.UtcNow;
        var sw = Stopwatch.StartNew();

        (Profile profile, string[] knownTickers) preload;
        try
        {
            preload = await PreloadProfileAndTickersAsync(tenantId, ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to preload profile/tickers for agent run.");
            throw;
        }

        var systemPrompt = !string.IsNullOrWhiteSpace(preload.profile.SystemPromptOverride)
            ? preload.profile.SystemPromptOverride!
            : SystemPrompts.Default;

        // A manual "Run now" does not go through the worker's pre-tick refresh, so the
        // latest price snapshot can fall outside MinPriceFreshnessSeconds and the agent
        // would see a $0 portfolio. Refresh prices + news first for manual runs only —
        // worker-triggered runs already refreshed in the same tick.
        if (trigger.Kind == RunTriggerKind.Manual)
        {
            try
            {
                var specs = await LoadTickerSpecsAsync(tenantId, ct);
                if (specs.Length > 0)
                {
                    await priceRefresh.RefreshAsync(specs, ct);
                    await newsRefresh.RefreshAsync(specs, ct);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Manual-run data refresh failed; proceeding with existing snapshots.");
            }
        }

        RunContext ctx;
        string inputJson;
        try
        {
            ctx = await assembler.BuildAsync(tenantId, trigger, ct);
            inputJson = JsonSerializer.Serialize(ctx, _camelIndented);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "ContextAssembler failed.");
            return await PersistFailureAsync(
                tenantId, startedAt, trigger, systemPrompt,
                structuredInputJson: "{}",
                rawBody: $"ContextAssembler threw: {ex}",
                model: string.Empty,
                latencyMs: (int)sw.ElapsedMilliseconds,
                ct: ct);
        }

        try
        {
            var result = await anthropic.AnalyzeAsync(systemPrompt, inputJson, ct);
            sw.Stop();

            var validated = TickerHallucinationValidator.Validate(result.Analysis, preload.knownTickers);

            return await PersistSuccessAsync(
                tenantId: tenantId,
                startedAt: startedAt,
                trigger: trigger,
                systemPromptUsed: systemPrompt,
                structuredInputJson: inputJson,
                rawResponseBody: result.RawResponseBody,
                analysis: validated,
                model: result.Model,
                inputTokens: result.InputTokens,
                outputTokens: result.OutputTokens,
                latencyMs: result.LatencyMs,
                parseFallbackUsed: result.ParseFallbackUsed,
                replayOfAdviceLogId: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger?.LogError(ex, "Anthropic call or parsing failed.");
            var raw = ex is AgentParseException ape
                ? ape.ResponseBody
                : ex.ToString();
            return await PersistFailureAsync(
                tenantId: tenantId,
                startedAt: startedAt,
                trigger: trigger,
                systemPromptUsed: systemPrompt,
                structuredInputJson: inputJson,
                rawBody: raw,
                model: string.Empty,
                latencyMs: (int)sw.ElapsedMilliseconds,
                ct: ct);
        }
    }

    public async Task<long> ReplayAsync(long sourceAdviceLogId, string systemPrompt, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var source = await db.AdviceLogs.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == sourceAdviceLogId, ct)
            ?? throw new InvalidOperationException(
                $"AdviceLog {sourceAdviceLogId} not found; cannot replay.");
        var tenantId = source.TenantId;

        var startedAt = clock.UtcNow;
        var sw = Stopwatch.StartNew();
        var (_, knownTickers) = await PreloadProfileAndTickersAsync(tenantId, ct);

        var trigger = new RunTrigger(RunTriggerKind.Manual,
            $"Replay of AdviceLog #{sourceAdviceLogId} with edited prompt");

        try
        {
            var result = await anthropic.AnalyzeAsync(systemPrompt, source.StructuredInputJson, ct);
            sw.Stop();
            var validated = TickerHallucinationValidator.Validate(result.Analysis, knownTickers);

            return await PersistSuccessAsync(
                tenantId: tenantId,
                startedAt: startedAt,
                trigger: trigger,
                systemPromptUsed: systemPrompt,
                structuredInputJson: source.StructuredInputJson,
                rawResponseBody: result.RawResponseBody,
                analysis: validated,
                model: result.Model,
                inputTokens: result.InputTokens,
                outputTokens: result.OutputTokens,
                latencyMs: result.LatencyMs,
                parseFallbackUsed: result.ParseFallbackUsed,
                replayOfAdviceLogId: sourceAdviceLogId,
                ct: ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger?.LogError(ex, "Replay failed for AdviceLog {Id}.", sourceAdviceLogId);
            var raw = ex is AgentParseException ape ? ape.ResponseBody : ex.ToString();
            return await PersistFailureAsync(
                tenantId: tenantId,
                startedAt: startedAt,
                trigger: trigger,
                systemPromptUsed: systemPrompt,
                structuredInputJson: source.StructuredInputJson,
                rawBody: raw,
                model: string.Empty,
                latencyMs: (int)sw.ElapsedMilliseconds,
                replayOfAdviceLogId: sourceAdviceLogId,
                ct: ct);
        }
    }

    private async Task<(Profile profile, string[] knownTickers)> PreloadProfileAndTickersAsync(int tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.Profiles.AsNoTracking()
            .SingleAsync(p => p.TenantId == tenantId, ct);
        var holdingTickers = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tenantId).Select(h => h.Ticker).ToListAsync(ct);
        var watchlistTickers = await db.WatchlistItems.AsNoTracking().Where(w => w.TenantId == tenantId).Select(w => w.Ticker).ToListAsync(ct);
        var known = holdingTickers.Concat(watchlistTickers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (profile, known);
    }

    private async Task<TickerSpec[]> LoadTickerSpecsAsync(int tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var holdings = await db.Holdings.AsNoTracking().Where(h => h.TenantId == tenantId)
            .Select(h => new TickerSpec(h.Ticker, h.AssetClass)).ToListAsync(ct);
        var watch = await db.WatchlistItems.AsNoTracking().Where(w => w.TenantId == tenantId)
            .Select(w => new TickerSpec(w.Ticker, w.AssetClass)).ToListAsync(ct);
        return holdings.Concat(watch)
            .DistinctBy(t => t.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<long> PersistSuccessAsync(
        int tenantId,
        DateTime startedAt,
        RunTrigger trigger,
        string systemPromptUsed,
        string structuredInputJson,
        string rawResponseBody,
        AgentAnalysis analysis,
        string model,
        int inputTokens,
        int outputTokens,
        int latencyMs,
        bool parseFallbackUsed,
        long? replayOfAdviceLogId,
        CancellationToken ct)
    {
        var row = new AdviceLog
        {
            TenantId = tenantId,
            TimestampUtc = startedAt,
            Trigger = trigger.Kind,
            TriggerDetail = trigger.Detail,
            StructuredInputJson = structuredInputJson,
            SystemPromptUsed = systemPromptUsed,
            RawResponseText = rawResponseBody,
            ParsedSummary = analysis.Summary,
            ParsedFlagsJson = JsonSerializer.Serialize(analysis.Flags, _camelIndented),
            ParsedDriftAlertsJson = JsonSerializer.Serialize(analysis.DriftAlerts, _camelIndented),
            ParsedConsiderationsJson = JsonSerializer.Serialize(analysis.Considerations, _camelIndented),
            ParsedPositionsJson = JsonSerializer.Serialize(analysis.Positions, _camelIndented),
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            LatencyMs = latencyMs,
            ParseFallbackUsed = parseFallbackUsed,
            ReplayOfAdviceLogId = replayOfAdviceLogId,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AdviceLogs.Add(row);
        await db.SaveChangesAsync(ct);
        logger?.LogInformation(
            "AdviceLog {Id} persisted ({Trigger}, model={Model}, tokens={In}/{Out}, latencyMs={Latency}, fallback={Fallback}).",
            row.Id, trigger.Kind, model, inputTokens, outputTokens, latencyMs, parseFallbackUsed);
        return row.Id;
    }

    private async Task<long> PersistFailureAsync(
        int tenantId,
        DateTime startedAt,
        RunTrigger trigger,
        string systemPromptUsed,
        string structuredInputJson,
        string rawBody,
        string model,
        int latencyMs,
        CancellationToken ct,
        long? replayOfAdviceLogId = null)
    {
        var row = new AdviceLog
        {
            TenantId = tenantId,
            TimestampUtc = startedAt,
            Trigger = trigger.Kind,
            TriggerDetail = trigger.Detail,
            StructuredInputJson = structuredInputJson,
            SystemPromptUsed = systemPromptUsed,
            RawResponseText = rawBody,
            ParsedSummary = "[error] Agent run failed; see RawResponseText for details.",
            ParsedFlagsJson = "[]",
            ParsedDriftAlertsJson = "[]",
            ParsedConsiderationsJson = "[]",
            ParsedPositionsJson = "[]",
            Model = model,
            InputTokens = 0,
            OutputTokens = 0,
            LatencyMs = latencyMs,
            ParseFallbackUsed = false,
            ReplayOfAdviceLogId = replayOfAdviceLogId,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AdviceLogs.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }
}
