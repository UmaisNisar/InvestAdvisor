namespace InvestAdvisor.Core.Abstractions;

public interface IRateLimiter
{
    /// <summary>Awaits permission for one request, blocking until a token is available.</summary>
    ValueTask WaitAsync(CancellationToken ct = default);
}
