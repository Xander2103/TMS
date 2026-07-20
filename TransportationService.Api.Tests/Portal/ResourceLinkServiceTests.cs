using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Entities;
using TransportationService.Api.Modules.Portal.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Portal;

/// <summary>
/// Favorites / recents / pins: upsert-on-touch, the bounded recent history, per-type
/// permission recheck on listing, dangling-target cleanup and strict user scoping.
/// </summary>
public class ResourceLinkServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubPermissions : IPermissionSetService
    {
        public IReadOnlySet<string> Codes { get; set; } = new HashSet<string>();

        public Task<IReadOnlySet<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Codes);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, ResourceLinkService Sut, StubPermissions Permissions, TestClock Clock,
        Guid TenantId, Guid UserId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "planner@acme.be", PasswordHash = "x",
            FirstName = "Piet", LastName = "Planner", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var permissions = new StubPermissions { Codes = new HashSet<string> { "customers.view", "orders.view" } };
        var sut = new ResourceLinkService(db.Context, tenant, new DevCurrentUserContext(userId), permissions, clock);
        return new Harness(db, sut, permissions, clock, tenantId, userId, customerId);
    }

    private static TouchResourceLinkRequest Favorite(Guid customerId, string label = "Haven BV") =>
        new(ResourceLinkKind.Favorite, "Customer", customerId, label, "KL-1", $"/customers/{customerId}");

    [Fact]
    public async Task Touch_CreatesOnce_AndRefreshesOnRepeat()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.TouchAsync(Favorite(h.CustomerId), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.Sut.TouchAsync(Favorite(h.CustomerId, "Haven BV (nieuw)"), CancellationToken.None);

        var link = Assert.Single(h.Db.Context.UserResourceLinks.AsNoTracking());
        Assert.Equal("Haven BV (nieuw)", link.Label);
        Assert.Equal(Now.UtcDateTime.AddMinutes(1), link.TouchedAt);
    }

    [Fact]
    public async Task Touch_UnknownEntityType_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.TouchAsync(
            new TouchResourceLinkRequest(ResourceLinkKind.Favorite, "PasswordHash", Guid.NewGuid(), "x", null, "/x"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Recents_AreCappedAt25()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        for (var i = 0; i < 30; i++)
        {
            var customerId = Guid.NewGuid();
            h.Db.Context.Customers.Add(new Customer
            {
                Id = customerId, TenantId = h.TenantId, CustomerNumber = $"KL-{i + 2}", Name = $"Klant {i}", IsActive = true,
            });
            await h.Db.Context.SaveChangesAsync();
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            await h.Sut.TouchAsync(
                new TouchResourceLinkRequest(ResourceLinkKind.Recent, "Customer", customerId, $"Klant {i}", null, $"/customers/{customerId}"),
                CancellationToken.None);
        }

        var recents = h.Db.Context.UserResourceLinks.AsNoTracking()
            .Where(l => l.Kind == ResourceLinkKind.Recent).ToList();
        Assert.Equal(25, recents.Count);
        // The newest survives; the oldest fell off.
        Assert.Contains(recents, l => l.Label == "Klant 29");
        Assert.DoesNotContain(recents, l => l.Label == "Klant 0");
    }

    [Fact]
    public async Task List_DropsTypesWithoutPermission_AndDanglingTargets()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.TouchAsync(Favorite(h.CustomerId), CancellationToken.None);

        // Permission revoked afterwards: the stored link no longer surfaces.
        h.Permissions.Codes = new HashSet<string>();
        Assert.Empty(await h.Sut.ListMineAsync(null, CancellationToken.None));

        // Permission restored but the customer got deleted: the dangling link is dropped too.
        h.Permissions.Codes = new HashSet<string> { "customers.view" };
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        h.Db.Context.Remove(customer); // soft delete via interceptor
        await h.Db.Context.SaveChangesAsync();
        Assert.Empty(await h.Sut.ListMineAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Links_AreUserScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.TouchAsync(Favorite(h.CustomerId), CancellationToken.None);
        var linkId = h.Db.Context.UserResourceLinks.AsNoTracking().Single().Id;

        // A second user in the same tenant sees nothing and cannot delete the first user's link.
        var otherUserId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = otherUserId, TenantId = h.TenantId, Email = "ander@acme.be", PasswordHash = "x",
            FirstName = "An", LastName = "Der", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        var otherSut = new ResourceLinkService(h.Db.Context, new DevTenantContext(h.TenantId),
            new DevCurrentUserContext(otherUserId), h.Permissions, h.Clock);

        Assert.Empty(await otherSut.ListMineAsync(null, CancellationToken.None));
        Assert.False(await otherSut.DeleteAsync(linkId, CancellationToken.None));
        Assert.True(await h.Sut.DeleteAsync(linkId, CancellationToken.None));
    }

    [Fact]
    public async Task Reorder_AppliesGivenSequence()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var secondCustomer = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer
        {
            Id = secondCustomer, TenantId = h.TenantId, CustomerNumber = "KL-2", Name = "Tweede BV", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var a = await h.Sut.TouchAsync(Favorite(h.CustomerId), CancellationToken.None);
        var b = await h.Sut.TouchAsync(Favorite(secondCustomer, "Tweede BV"), CancellationToken.None);

        await h.Sut.ReorderAsync([b.Id, a.Id], CancellationToken.None);

        var ordered = await h.Sut.ListMineAsync(ResourceLinkKind.Favorite, CancellationToken.None);
        Assert.Equal(new[] { b.Id, a.Id }, ordered.Select(l => l.Id).ToArray());
    }
}
