using FluentAssertions;
using InvestAdvisor.Data.Providers.Yahoo;
using InvestAdvisor.Test.TestHelpers;
using Xunit;

namespace InvestAdvisor.Test.Providers;

public class YahooNewsProviderTests
{
    private static readonly DateTime Now = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    private static (YahooNewsProvider sut, StubHttpMessageHandler handler) BuildSut(string responseBody)
    {
        var handler = new StubHttpMessageHandler { ResponseBody = responseBody, MediaType = "application/xml" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://feeds.finance.yahoo.com/") };
        return (new YahooNewsProvider(http, new FakeSystemClock(Now)), handler);
    }

    private static string Rss(params (string Title, string Link, string PubDate)[] items)
    {
        var xml = string.Join("", items.Select(i =>
            $"<item><title>{i.Title}</title><link>{i.Link}</link><pubDate>{i.PubDate}</pubDate></item>"));
        return $"""<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel>{xml}</channel></rss>""";
    }

    [Fact]
    public async Task Parses_rss_and_tags_rows_with_the_requested_broker_form_ticker()
    {
        var (sut, _) = BuildSut(Rss(
            ("Micron earnings beat", "https://news.example/1", "Thu, 11 Jun 2026 10:00:00 +0000"),
            ("Semis roundup", "https://news.example/2", "Thu, 11 Jun 2026 07:00:00 +0000")));

        var headlines = await sut.GetTickerNewsAsync("mu.to");

        headlines.Should().HaveCount(2);
        headlines[0].Headline.Should().Be("Micron earnings beat");
        headlines[0].Ticker.Should().Be("MU.TO"); // broker form, so it joins against holdings
        headlines[0].Source.Should().Be("Yahoo Finance");
        headlines[0].PublishedAtUtc.Should().Be(new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Share_class_ticker_is_translated_to_yahoo_symbol_in_the_request()
    {
        var (sut, handler) = BuildSut(Rss());

        await sut.GetTickerNewsAsync("IDIV.B.TO");

        handler.LastRequest!.RequestUri!.Query.Should().Contain("s=IDIV-B.TO");
    }

    [Fact]
    public async Task Items_older_than_the_lookback_or_undatable_are_dropped()
    {
        var (sut, _) = BuildSut(Rss(
            ("fresh", "https://news.example/1", "Thu, 11 Jun 2026 11:00:00 +0000"),
            ("ancient", "https://news.example/2", "Mon, 11 May 2026 11:00:00 +0000"),
            ("undatable", "https://news.example/3", "not a date")));

        var headlines = await sut.GetTickerNewsAsync("MU.TO");

        headlines.Should().ContainSingle(h => h.Headline == "fresh");
    }

    [Fact]
    public async Task Degrades_to_empty_on_malformed_xml()
    {
        var (sut, _) = BuildSut("this is not xml <<<");

        var headlines = await sut.GetTickerNewsAsync("MU.TO");

        headlines.Should().BeEmpty();
    }

    [Fact]
    public async Task Market_news_is_intentionally_empty()
    {
        var (sut, handler) = BuildSut(Rss());

        var headlines = await sut.GetMarketNewsAsync();

        headlines.Should().BeEmpty();
        handler.CallCount.Should().Be(0); // no request — market news stays with Finnhub
    }
}
