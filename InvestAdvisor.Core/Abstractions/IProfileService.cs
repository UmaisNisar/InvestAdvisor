using InvestAdvisor.Core.Entities;

namespace InvestAdvisor.Core.Abstractions;

public interface IProfileService
{
    Task<Profile> GetAsync(CancellationToken ct = default);
    Task<Profile> UpdateAsync(Profile updated, CancellationToken ct = default);
}
