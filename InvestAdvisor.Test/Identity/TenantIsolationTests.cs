using FluentAssertions;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Data.Identity;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Test.TestHelpers;
using Xunit;

namespace InvestAdvisor.Test.Identity;

/// <summary>
/// The core multi-tenancy guarantee: one tenant never sees another's data. Drives the real
/// TenantContext (email → tenant, provisioning on first touch) + the scoped services.
/// </summary>
public class TenantIsolationTests
{
    private sealed class FakeUser(string? email) : ICurrentUserAccessor
    {
        public Task<string?> GetEmailAsync(CancellationToken ct = default) => Task.FromResult(email);
    }

    // Each "request" gets its own TenantContext (it caches the resolved tenant for the scope).
    private static HoldingsService HoldingsFor(SqliteFixture db, string email)
        => new(db.Factory, new TenantContext(new FakeUser(email), db.Factory));

    private static WatchlistService WatchlistFor(SqliteFixture db, string email)
        => new(db.Factory, new TenantContext(new FakeUser(email), db.Factory));

    private static Holding NewHolding(string ticker) => new()
    {
        Ticker = ticker, Name = ticker, AssetClass = AssetClass.Equity,
        AccountType = AccountType.Taxable, Quantity = 1m, AvgCost = 1m, Currency = "USD",
    };

    [Fact]
    public async Task Each_tenant_sees_only_its_own_holdings()
    {
        await using var db = new SqliteFixture();

        await HoldingsFor(db, "alice@example.com").CreateAsync(NewHolding("AAPL"));
        await HoldingsFor(db, "bob@example.com").CreateAsync(NewHolding("TSLA"));

        (await HoldingsFor(db, "alice@example.com").ListAsync())
            .Select(h => h.Ticker).Should().Equal("AAPL");
        (await HoldingsFor(db, "bob@example.com").ListAsync())
            .Select(h => h.Ticker).Should().Equal("TSLA");
    }

    [Fact]
    public async Task A_tenant_cannot_delete_another_tenants_holding()
    {
        await using var db = new SqliteFixture();

        var aliceHolding = await HoldingsFor(db, "alice@example.com").CreateAsync(NewHolding("AAPL"));

        // Bob tries to delete Alice's holding by id — must be a no-op (scoped by tenant).
        await HoldingsFor(db, "bob@example.com").DeleteAsync(aliceHolding.Id);

        (await HoldingsFor(db, "alice@example.com").ListAsync())
            .Select(h => h.Ticker).Should().Equal("AAPL");
    }

    [Fact]
    public async Task Same_email_resolves_to_the_same_tenant_case_insensitively()
    {
        await using var db = new SqliteFixture();

        await WatchlistFor(db, "Alice@Example.com")
            .CreateAsync(new WatchlistItem { Ticker = "NVDA", AssetClass = AssetClass.Equity });

        // Different casing → same tenant → sees the item.
        (await WatchlistFor(db, "alice@example.com").ListAsync())
            .Select(w => w.Ticker).Should().Equal("NVDA");
    }

    [Fact]
    public async Task First_touch_provisions_a_tenant_and_default_profile()
    {
        await using var db = new SqliteFixture();

        var tenantCtx = new TenantContext(new FakeUser("new@example.com"), db.Factory);
        var tid = await tenantCtx.GetTenantIdAsync();

        await using var c = db.CreateContext();
        c.Tenants.Should().ContainSingle(t => t.Email == "new@example.com");
        c.Profiles.Count(p => p.TenantId == tid).Should().Be(1);
    }
}
