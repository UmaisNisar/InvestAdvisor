namespace InvestAdvisor.Core.Abstractions;

public interface ISystemClock
{
    DateTime UtcNow { get; }
}
