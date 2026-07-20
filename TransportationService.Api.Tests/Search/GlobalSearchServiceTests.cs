using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Search.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Search;

public class GlobalSearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId)
    {
        public GlobalSearchService Sut() => new(
            Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(UserId));
    }

    /// <summary>User with ONLY orders.view; an ACME order and an ACME customer both match "acme".</summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "T", Slug = "t", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "u@t.be", FirstName = "U", LastName = "Ser", IsActive = true });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Kijker", IsActive = true });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "orders.view", Module = "orders", Action = "view", Description = "x" });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });

        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Acme BV" });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-0001",
            CustomerId = customerId,
            CustomerReference = "ACME-REF-9",
            OrderDate = new DateOnly(2026, 7, 15),
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId);
    }

    [Fact]
    public async Task Search_OnlyReturnsCategoriesTheUserMayView()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var hits = await h.Sut().SearchAsync("acme", CancellationToken.None);

        // The order matches on customer reference; the customer itself is hidden
        // because the user lacks customers.view.
        var hit = Assert.Single(hits);
        Assert.Equal("Transportopdrachten", hit.Category);
        Assert.Equal("ORD-0001", hit.Title);
        Assert.StartsWith("/transport-orders/", hit.Route);
    }

    [Fact]
    public async Task Search_RequiresAtLeastTwoCharacters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Empty(await h.Sut().SearchAsync("a", CancellationToken.None));
        Assert.Empty(await h.Sut().SearchAsync("  ", CancellationToken.None));
    }

    [Fact]
    public async Task Search_IsTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Same user id, different tenant context → that tenant has no data (and the user's
        // permission join still resolves via the user's own roles, but hits are tenant-bound).
        var sut = new GlobalSearchService(h.Db.Context, new DevTenantContext(Guid.NewGuid()), new DevCurrentUserContext(h.UserId));
        Assert.Empty(await sut.SearchAsync("acme", CancellationToken.None));
    }
}
