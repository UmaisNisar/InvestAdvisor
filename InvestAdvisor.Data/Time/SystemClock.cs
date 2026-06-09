using InvestAdvisor.Core.Abstractions;

namespace InvestAdvisor.Data.Time;

public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
