using FluentAssertions;
using InvestAdvisor.Core.Agent;
using Xunit;

namespace InvestAdvisor.Test.Agent;

public class ModelPricingTests
{
    [Theory]
    [InlineData("claude-sonnet-4-6", 3, 15)]
    [InlineData("claude-haiku-4-5", 1, 5)]
    [InlineData("claude-opus-4-6", 5, 25)]
    [InlineData("claude-future-model", 3, 15)] // unknown claude id falls back to Sonnet rates
    public void Claude_models_price_at_anthropic_list_rates(string model, decimal inPerM, decimal outPerM)
    {
        var usd = ModelPricing.EstimateUsd(model, 1_000_000, 1_000_000);
        usd.Should().Be(inPerM + outPerM);
    }

    [Theory]
    [InlineData("gemini-2.5-flash")]
    [InlineData("gemini-2.5-flash-lite")]
    [InlineData("llama-3.3-70b-versatile")]
    [InlineData("deepseek/deepseek-chat-v3.1:free")]
    [InlineData("llama3.2")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_claude_models_are_free(string? model)
    {
        ModelPricing.EstimateUsd(model, 5_000_000, 5_000_000).Should().Be(0m);
    }
}
