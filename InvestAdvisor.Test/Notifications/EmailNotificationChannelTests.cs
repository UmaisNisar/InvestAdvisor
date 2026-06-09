using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;
using InvestAdvisor.Core.Notifications;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Test.TestHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InvestAdvisor.Test.Notifications;

public class EmailNotificationChannelTests
{
    private static AgentAnalysis MakeAnalysis(
        FlagSeverity? flagSev = null,
        DriftSeverity? driftSev = null)
    {
        var flags = flagSev is null
            ? Array.Empty<Flag>()
            : new[] { new Flag(flagSev.Value, "AAPL", "t", "d", null) };
        var drift = driftSev is null
            ? Array.Empty<DriftAlert>()
            : new[] { new DriftAlert(driftSev.Value, "AAPL", 60, 50, 10, "note") };
        return new AgentAnalysis("s", flags, drift, Array.Empty<Consideration>(),
            new AgentRunMetrics("m", 0, 0, 0, false), Array.Empty<PositionCall>());
    }

    private static (EmailNotificationChannel sut, ISmtpClient smtp, IRuntimeSettingsStore settings)
        BuildSut(bool emailEnabled = true, string? host = "smtp.example.com")
    {
        var smtp = Substitute.For<ISmtpClient>();
        var settingsStore = Substitute.For<IRuntimeSettingsStore>();
        settingsStore.GetAsync(Arg.Any<CancellationToken>())
                     .Returns(new ValueTask<RuntimeSettings>(new RuntimeSettings
                     {
                         EmailEnabled = emailEnabled,
                         SmtpHost = host,
                         SmtpFrom = "me@example.com",
                         SmtpTo = "me@example.com",
                         SmtpPort = 587,
                         SmtpEnableSsl = true,
                     }));
        var smtpOpts = Options.Create(new SmtpOptions { Password = "secret" });
        var clock = new FakeSystemClock(DateTime.UtcNow);
        return (new EmailNotificationChannel(settingsStore, smtpOpts, smtp, clock), smtp, settingsStore);
    }

    [Theory]
    [InlineData(FlagSeverity.Info, null, false)]
    [InlineData(FlagSeverity.Warn, null, true)]
    [InlineData(FlagSeverity.Critical, null, true)]
    [InlineData(null, DriftSeverity.Note, false)]
    [InlineData(null, DriftSeverity.ActionSuggested, true)]
    public void ShouldDispatch_returns_true_only_on_warn_critical_or_action_suggested(
        FlagSeverity? flagSev, DriftSeverity? driftSev, bool expected)
    {
        var (sut, _, _) = BuildSut();

        sut.ShouldDispatch(MakeAnalysis(flagSev, driftSev)).Should().Be(expected);
    }

    [Fact]
    public async Task SendAsync_returns_Sent_on_success()
    {
        var (sut, smtp, _) = BuildSut();
        var row = new AdviceLog { Id = 1, TriggerDetail = "test", Trigger = RunTriggerKind.BigMove };

        var delivery = await sut.SendAsync(row, MakeAnalysis(FlagSeverity.Warn));

        delivery.Status.Should().Be(DeliveryStatus.Sent);
        delivery.Channel.Should().Be("Email");
        delivery.DeliveredAtUtc.Should().NotBeNull();
        await smtp.Received(1).SendAsync(Arg.Any<SmtpMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_returns_Failed_on_smtp_exception()
    {
        var (sut, smtp, _) = BuildSut();
        smtp.SendAsync(Arg.Any<SmtpMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("auth failed")));
        var row = new AdviceLog { Id = 2, TriggerDetail = "test", Trigger = RunTriggerKind.BigMove };

        var delivery = await sut.SendAsync(row, MakeAnalysis(FlagSeverity.Critical));

        delivery.Status.Should().Be(DeliveryStatus.Failed);
        delivery.ErrorMessage.Should().Contain("auth failed");
    }

    [Fact]
    public async Task SendAsync_returns_Skipped_when_email_disabled()
    {
        var (sut, smtp, _) = BuildSut(emailEnabled: false);
        var row = new AdviceLog { Id = 3, TriggerDetail = "test", Trigger = RunTriggerKind.BigMove };

        var delivery = await sut.SendAsync(row, MakeAnalysis(FlagSeverity.Critical));

        delivery.Status.Should().Be(DeliveryStatus.Skipped);
        await smtp.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
