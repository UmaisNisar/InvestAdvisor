using InvestAdvisor.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>
/// Per-test SQLite in-memory database. Keeps a single connection open for the lifetime
/// of the fixture so all <see cref="InvestAdvisorDbContext"/> instances share the same DB.
/// </summary>
public sealed class SqliteFixture : IDisposable, IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public IDbContextFactory<InvestAdvisorDbContext> Factory { get; }

    public SqliteFixture()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        Factory = new TestDbContextFactory(Connection);

        using var ctx = Factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public InvestAdvisorDbContext CreateContext() => Factory.CreateDbContext();

    public void Dispose() => Connection.Dispose();
    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    private sealed class TestDbContextFactory(SqliteConnection connection)
        : IDbContextFactory<InvestAdvisorDbContext>
    {
        public InvestAdvisorDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<InvestAdvisorDbContext>()
                .UseSqlite(connection)
                .Options;
            return new InvestAdvisorDbContext(options);
        }
    }
}
