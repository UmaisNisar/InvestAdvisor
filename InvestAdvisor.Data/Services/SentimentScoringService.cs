using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestAdvisor.Data.Services;

/// <summary>
/// LLM batch sentiment scorer. Picks up <see cref="NewsItem"/>s with no sentiment yet, scores them
/// in chunks via the cheap routine model, and writes the scores back. Also computes the recency-
/// weighted per-ticker aggregate consumed by the screener and the agent context. Respects the same
/// pause / daily-budget guards as the rest of the AI surface, and logs each batch as a
/// <see cref="SentimentRun"/> so its spend counts toward the budget.
/// </summary>
public sealed class SentimentScoringService(
    IDbContextFactory<InvestAdvisorDbContext> dbFactory,
    ILlmClient llm,
    IRuntimeSettingsStore settingsStore,
    ICostService costService,
    ISystemClock clock,
    ILogger<SentimentScoringService>? logger = null) : ISentimentScoringService
{
    private const int BatchSize = 30;
    private const int MaxItemsPerRun = 240; // bound cost: ~8 batches/run
    private const int MaxTextLength = 240;
    private static readonly TimeSpan AggregateWindow = TimeSpan.FromHours(72);

    public async Task<int> ScoreUnscoredAsync(CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (settings.AgentPaused)
        {
            logger?.LogInformation("Agent paused; skipping sentiment scoring.");
            return 0;
        }
        if (await costService.IsOverDailyBudgetAsync(ct))
        {
            logger?.LogWarning("Daily AI budget reached; skipping sentiment scoring.");
            return 0;
        }

        List<NewsItem> pending;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            pending = await db.NewsItems
                .Where(n => n.SentimentScoredAtUtc == null)
                .OrderByDescending(n => n.PublishedAtUtc)
                .Take(MaxItemsPerRun)
                .ToListAsync(ct);
        }
        if (pending.Count == 0) return 0;

        var scored = 0;
        for (var offset = 0; offset < pending.Count; offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            // Re-check budget between batches so a long run stops as soon as the cap is hit.
            if (offset > 0 && await costService.IsOverDailyBudgetAsync(ct))
            {
                logger?.LogWarning("Daily AI budget reached mid-run; stopping sentiment scoring.");
                break;
            }

            var batch = pending.Skip(offset).Take(BatchSize).ToList();
            var texts = batch.Select(ToScoringText).ToList();

            SentimentBatchResult result;
            try { result = await llm.ScoreSentimentAsync(texts, ct: ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Sentiment batch failed ({Count} items); continuing.", batch.Count);
                continue;
            }

            var now = clock.UtcNow;
            var byIndex = result.Scores
                .Where(s => s.Index >= 0 && s.Index < batch.Count)
                .GroupBy(s => s.Index)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (item, i) in batch.Select((x, i) => (x, i)))
            {
                if (byIndex.TryGetValue(i, out var s))
                {
                    item.SentimentScore = s.Score;
                    item.SentimentLabel = s.Label;
                }
                else
                {
                    // Model omitted this index — mark neutral so we don't re-score it forever.
                    item.SentimentScore = 0m;
                    item.SentimentLabel = "neutral";
                }
                item.SentimentScoredAtUtc = now;
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.NewsItems.AttachRange(batch);
            foreach (var item in batch)
            {
                db.Entry(item).Property(x => x.SentimentScore).IsModified = true;
                db.Entry(item).Property(x => x.SentimentLabel).IsModified = true;
                db.Entry(item).Property(x => x.SentimentScoredAtUtc).IsModified = true;
            }
            db.SentimentRuns.Add(new SentimentRun
            {
                GeneratedAtUtc = now,
                ItemsScored = batch.Count,
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                LatencyMs = result.LatencyMs,
            });
            await db.SaveChangesAsync(ct);
            scored += batch.Count;
        }

        if (scored > 0) logger?.LogInformation("Scored sentiment for {Count} news/social items.", scored);
        return scored;
    }

    public async Task<IReadOnlyDictionary<string, TickerSentiment>> GetTickerSentimentAsync(
        CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - AggregateWindow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.NewsItems.AsNoTracking()
            .Where(n => n.Ticker != null && n.SentimentScore != null && n.PublishedAtUtc >= cutoff)
            .Select(n => new { n.Ticker, n.SentimentScore, n.PublishedAtUtc })
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var windowHours = (decimal)AggregateWindow.TotalHours;

        return rows
            .GroupBy(r => r.Ticker!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                decimal weightedSum = 0m, weightTotal = 0m;
                foreach (var r in g)
                {
                    // Linear recency weight: newest ≈ 1.0, oldest in window ≈ 0.1 (never zero).
                    var ageHours = (decimal)(now - r.PublishedAtUtc).TotalHours;
                    var w = Math.Clamp(1m - ageHours / windowHours, 0.1m, 1m);
                    weightedSum += r.SentimentScore!.Value * w;
                    weightTotal += w;
                }
                var mean = weightTotal > 0m ? Math.Round(weightedSum / weightTotal, 3) : 0m;
                var label = mean > 0.15m ? "bullish" : mean < -0.15m ? "bearish" : "neutral";
                return (Ticker: g.Key, Sentiment: new TickerSentiment(mean, g.Count(), label));
            })
            .ToDictionary(x => x.Ticker, x => x.Sentiment, StringComparer.OrdinalIgnoreCase);
    }

    private static string ToScoringText(NewsItem n)
    {
        var ticker = string.IsNullOrWhiteSpace(n.Ticker) ? "MARKET" : n.Ticker!.Trim().ToUpperInvariant();
        var headline = n.Headline.Length > MaxTextLength ? n.Headline[..MaxTextLength] : n.Headline;
        return $"{ticker}: {headline}";
    }
}
