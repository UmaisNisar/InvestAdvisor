namespace InvestAdvisor.Core.Entities;

/// <summary>A row that belongs to exactly one tenant and is addressed by its own integer id.</summary>
public interface ITenantOwned
{
    int Id { get; }
    int TenantId { get; set; }
}
