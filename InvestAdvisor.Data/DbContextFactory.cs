using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InvestAdvisor.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` when generating / applying migrations.
/// At design time we don't have the runtime DI graph, so we build a fixed connection
/// string against a temporary "migrations" SQLite file in the user's LocalAppData.
/// </summary>
public sealed class DbContextFactory : IDesignTimeDbContextFactory<InvestAdvisorDbContext>
{
    public InvestAdvisorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InvestAdvisorDbContext>()
            .UseSqlite(InvestAdvisorDbContext.GetDefaultConnectionString())
            .Options;
        return new InvestAdvisorDbContext(options);
    }
}
