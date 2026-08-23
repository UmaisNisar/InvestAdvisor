using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using NSubstitute;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>An <see cref="IRuntimeSettingsStore"/> that always returns the given settings.</summary>
internal static class FakeSettingsStore
{
    public static IRuntimeSettingsStore For(RuntimeSettings settings)
    {
        var store = Substitute.For<IRuntimeSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<RuntimeSettings>(settings));
        return store;
    }

    public static IRuntimeSettingsStore For(Action<RuntimeSettings>? mutate = null)
    {
        var s = new RuntimeSettings();
        mutate?.Invoke(s);
        return For(s);
    }
}
