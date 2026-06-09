using InvestAdvisor.Core.Abstractions;

namespace InvestAdvisor.Test.TestHelpers;

public sealed class FakeSystemClock(DateTime initial) : ISystemClock
{
    public DateTime UtcNow { get; set; } = initial;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
