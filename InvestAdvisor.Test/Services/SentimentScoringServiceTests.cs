using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Services;

public class SentimentScoringServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private static (SentimentScoringService sut, IAnthropicClient anthropic, ICostService cost, IRuntimeSettingsStore store)
        BuildSut(SqliteFixture db, bool paused = false, bool overBudget = false)
    {
        var anthropic = Substitute.For<IAnthropicClient>();
        var store = Substitute.For<IRuntimeSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>())
             .Returns(new ValueTask<RuntimeSettings>(new RuntimeSettings { AgentPaused = paused }));
        var cost = Substitute.For<ICostService>();
        cost.IsOverDailyBudgetAsync(Arg.Any<CancellationToken>()).Returns(overBudget);
        var sut = new SentimentScoringService(db.Factory, anthropic, store, cost, new FakeSystemClock(Now));
        return (sut, anthropic, cost, store);
    }

    private static NewsItem Unscored(string ticker, string url, DateTime? published = null) => new()
    {
        Ticker = ticker, Headline = $"{ticker} headline", Source = "News", Url = url,
        Channel = NewsSource.News, PublishedAtUtc = published ?? Now, FetchedAtUtc = published ?? Now,
    };

    [Fact]
    public async Task Scores_pending_items_and_logs_a_run()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.NewsItems.AddRange(Unscored("AAPL", "u1"), Unscored("TSLA", "u2"));
            c.SaveChanges();
        }
        var (sut, anthropic, _, _) = BuildSut(db);
        anthropic.ScoreSentimentAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                 .Returns(new SentimentBatchResult(
                     new[] { new SentimentScore(0, 0.7m, "bullish"), new SentimentScore(1, -0.4m, "bearish") },
                     "{}", "claude-haiku-4-5", 20, 8, 100, false));

        var scored = await sut.ScoreUnscoredAsync();

        scored.Should().Be(2);
        using var verify = db.CreateContext();
        var rows = await verify.NewsItems.OrderBy(n => n.Url).ToListAsync();
        rows[0].SentimentScore.Should().Be(0.7m);
        rows[0].SentimentLabel.Should().Be("bullish");
        rows[0].SentimentScoredAtUtc.Should().Be(Now);
        rows[1].SentimentScore.Should().Be(-0.4m);

        var run = await verify.SentimentRuns.SingleAsync();
        run.ItemsScored.Should().Be(2);
        run.InputTokens.Should().Be(20);
        run.Model.Should().Be("claude-haiku-4-5");
    }

    [Fact]
    public async Task Omitted_index_is_marked_neutral_so_it_is_not_rescored()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            c.NewsItems.AddRange(Unscored("AAPL", "u1"), Unscored("TSLA", "u2"));
            c.SaveChanges();
        }
        var (sut, anthropic, _, _) = BuildSut(db);
        // Model returns a score only for index 0.
        anthropic.ScoreSentimentAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                 .Returns(new SentimentBatchResult(
                     new[] { new SentimentScore(0, 0.5m, "bullish") },
                     "{}", "claude-haiku-4-5", 1, 1, 1, false));

        await sut.ScoreUnscoredAsync();

        using var verify = db.CreateContext();
        (await verify.NewsItems.CountAsync(n => n.SentimentScoredAtUtc == null)).Should().Be(0);
        var tsla = await verify.NewsItems.SingleAsync(n => n.Url == "u2");
        tsla.SentimentScore.Should().Be(0m);
        tsla.SentimentLabel.Should().Be("neutral");
    }

    [Fact]
    public async Task Skips_when_paused()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext()) { c.NewsItems.Add(Unscored("AAPL", "u1")); c.SaveChanges(); }
        var (sut, anthropic, _, _) = BuildSut(db, paused: true);

        var scored = await sut.ScoreUnscoredAsync();

        scored.Should().Be(0);
        await anthropic.DidNotReceive().ScoreSentimentAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_when_over_budget()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext()) { c.NewsItems.Add(Unscored("AAPL", "u1")); c.SaveChanges(); }
        var (sut, anthropic, _, _) = BuildSut(db, overBudget: true);

        var scored = await sut.ScoreUnscoredAsync();

        scored.Should().Be(0);
        await anthropic.DidNotReceive().ScoreSentimentAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aggregate_excludes_unscored_and_old_rows_and_means_recent()
    {
        await using var db = new SqliteFixture();
        using (var c = db.CreateContext())
        {
            // Two scored AAPL rows at Now -> equal recency weight -> simple mean (1.0 + 0.0)/2 = 0.5
            var a1 = Unscored("AAPL", "a1"); a1.SentimentScore = 1.0m; a1.SentimentScoredAtUtc = Now;
            var a2 = Unscored("AAPL", "a2"); a2.SentimentScore = 0.0m; a2.SentimentScoredAtUtc = Now;
            // Unscored row should be ignored
            var a3 = Unscored("AAPL", "a3");
            // Old row outside the 72h window should be ignored
            var a4 = Unscored("AAPL", "a4", Now.AddDays(-5)); a4.SentimentScore = -1.0m; a4.SentimentScoredAtUtc = Now.AddDays(-5);
            c.NewsItems.AddRange(a1, a2, a3, a4);
            c.SaveChanges();
        }
        var (sut, _, _, _) = BuildSut(db);

        var agg = await sut.GetTickerSentimentAsync();

        agg.Should().ContainKey("AAPL");
        agg["AAPL"].MeanScore.Should().Be(0.5m);
        agg["AAPL"].PostCount.Should().Be(2);
        agg["AAPL"].Label.Should().Be("bullish");
    }
}
