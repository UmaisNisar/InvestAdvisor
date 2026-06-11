using FluentValidation.TestHelper;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Ui.Validation;
using Xunit;

namespace InvestAdvisor.Test.Validation;

public class HoldingValidatorTests
{
    private readonly HoldingValidator _validator = new();

    private static Holding ValidHolding() => new()
    {
        Ticker = "SHOP.TO",
        Name = "Shopify",
        Currency = "CAD",
        Quantity = 10m,
        AvgCost = 95.5m,
    };

    [Fact]
    public void Valid_holding_passes()
        => _validator.TestValidate(ValidHolding()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BAD TICKER")]
    [InlineData("WAY.TOO.LONG.TICKER.SYMBOL")]
    public void Invalid_ticker_fails(string ticker)
    {
        var h = ValidHolding();
        h.Ticker = ticker;
        _validator.TestValidate(h).ShouldHaveValidationErrorFor(x => x.Ticker);
    }

    [Theory]
    [InlineData("BRK-B")]
    [InlineData("BTCC.B.TO")]
    [InlineData(" aapl ")] // whitespace is trimmed on save
    public void Real_world_tickers_pass(string ticker)
    {
        var h = ValidHolding();
        h.Ticker = ticker;
        _validator.TestValidate(h).ShouldNotHaveValidationErrorFor(x => x.Ticker);
    }

    [Fact]
    public void Zero_quantity_fails()
    {
        var h = ValidHolding();
        h.Quantity = 0m;
        _validator.TestValidate(h).ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Negative_avg_cost_fails()
    {
        var h = ValidHolding();
        h.AvgCost = -1m;
        _validator.TestValidate(h).ShouldHaveValidationErrorFor(x => x.AvgCost);
    }

    [Fact]
    public void Target_allocation_above_100_fails()
    {
        var h = ValidHolding();
        h.TargetAllocationPct = 101m;
        _validator.TestValidate(h).ShouldHaveValidationErrorFor(x => x.TargetAllocationPct);
    }

    [Fact]
    public void Missing_target_allocation_is_fine()
    {
        var h = ValidHolding();
        h.TargetAllocationPct = null;
        _validator.TestValidate(h).ShouldNotHaveValidationErrorFor(x => x.TargetAllocationPct);
    }
}

public class WatchlistItemValidatorTests
{
    private readonly WatchlistItemValidator _validator = new();

    [Fact]
    public void Valid_item_passes()
        => _validator.TestValidate(new WatchlistItem { Ticker = "AC.TO", PriceTargetLow = 15m, PriceTargetHigh = 25m })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Empty_ticker_fails()
        => _validator.TestValidate(new WatchlistItem { Ticker = "" })
            .ShouldHaveValidationErrorFor(x => x.Ticker);

    [Fact]
    public void High_target_below_low_target_fails()
        => _validator.TestValidate(new WatchlistItem { Ticker = "AC.TO", PriceTargetLow = 25m, PriceTargetHigh = 15m })
            .ShouldHaveValidationErrorFor(x => x.PriceTargetHigh);

    [Fact]
    public void Only_one_target_is_fine()
        => _validator.TestValidate(new WatchlistItem { Ticker = "AC.TO", PriceTargetHigh = 15m })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Zero_price_target_fails()
        => _validator.TestValidate(new WatchlistItem { Ticker = "AC.TO", PriceTargetLow = 0m })
            .ShouldHaveValidationErrorFor(x => x.PriceTargetLow);
}

public class ProfileValidatorTests
{
    private readonly ProfileValidator _validator = new();

    [Fact]
    public void Defaults_pass()
        => _validator.TestValidate(new Profile()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Drift_threshold_above_100_fails()
        => _validator.TestValidate(new Profile { DriftPctThreshold = 150m })
            .ShouldHaveValidationErrorFor(x => x.DriftPctThreshold);

    [Fact]
    public void Zero_cadence_fails()
        => _validator.TestValidate(new Profile { RebalanceCadenceHours = 0 })
            .ShouldHaveValidationErrorFor(x => x.RebalanceCadenceHours);
}

public class RuntimeSettingsValidatorTests
{
    private readonly RuntimeSettingsValidator _validator = new();

    [Fact]
    public void Defaults_pass()
        => _validator.TestValidate(new RuntimeSettings()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Unknown_time_zone_fails()
        => _validator.TestValidate(new RuntimeSettings { TimeZoneId = "Mars/Olympus_Mons" })
            .ShouldHaveValidationErrorFor(x => x.TimeZoneId);

    [Fact]
    public void Custom_provider_requires_base_url()
        => _validator.TestValidate(new RuntimeSettings { LlmProvider = LlmProviders.Custom, LlmCustomBaseUrl = null })
            .ShouldHaveValidationErrorFor(x => x.LlmCustomBaseUrl);

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/feed")]
    public void Non_http_base_url_fails(string url)
        => _validator.TestValidate(new RuntimeSettings { LlmProvider = LlmProviders.Custom, LlmCustomBaseUrl = url })
            .ShouldHaveValidationErrorFor(x => x.LlmCustomBaseUrl);

    [Fact]
    public void Valid_custom_base_url_passes()
        => _validator.TestValidate(new RuntimeSettings { LlmProvider = LlmProviders.Custom, LlmCustomBaseUrl = "https://api.groq.com/openai/v1/" })
            .ShouldNotHaveValidationErrorFor(x => x.LlmCustomBaseUrl);

    [Fact]
    public void Email_enabled_requires_host_from_and_to()
    {
        var result = _validator.TestValidate(new RuntimeSettings { EmailEnabled = true });
        result.ShouldHaveValidationErrorFor(x => x.SmtpHost);
        result.ShouldHaveValidationErrorFor(x => x.SmtpFrom);
        result.ShouldHaveValidationErrorFor(x => x.SmtpTo);
    }

    [Fact]
    public void Invalid_email_address_fails_even_when_email_disabled()
        => _validator.TestValidate(new RuntimeSettings { EmailEnabled = false, SmtpFrom = "not-an-email" })
            .ShouldHaveValidationErrorFor(x => x.SmtpFrom);

    [Fact]
    public void Invalid_holdings_csv_url_fails()
        => _validator.TestValidate(new RuntimeSettings { HoldingsCsvUrl = "definitely not a url" })
            .ShouldHaveValidationErrorFor(x => x.HoldingsCsvUrl);

    [Fact]
    public void Weight_above_100_fails()
        => _validator.TestValidate(new RuntimeSettings { WeightGrowth = 250 })
            .ShouldHaveValidationErrorFor(x => x.WeightGrowth);

    [Fact]
    public void Negative_budget_fails()
        => _validator.TestValidate(new RuntimeSettings { DailyBudgetUsd = -1m })
            .ShouldHaveValidationErrorFor(x => x.DailyBudgetUsd);
}
