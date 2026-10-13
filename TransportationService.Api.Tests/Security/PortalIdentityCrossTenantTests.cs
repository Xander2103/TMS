using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Fix wave B, item B7 (pass-2 test review, requirement 8). Wave 1 changed three permission
/// evaluators and added no cross-tenant case to any of them, so the coverage table scored the
/// tenant axis "weak". This file closes that with REAL second-tenant rows — two tenants that are
/// deliberately symmetric (same permission codes, same role name, same customer shape), so a
/// leak shows up as a wrong answer rather than as an empty result that would also appear with a
/// random Guid.
///
/// <b>Why the query filter is not the mechanism here.</b> <c>User</c>, <c>Role</c>,
/// <c>UserRole</c> and <c>RolePermission</c> deliberately do NOT implement <c>ITenantOwned</c>, so
/// the H1 global filter never applies to them — authentication has to resolve an account *before*
/// a tenant scope exists (`AuthService` runs at login, `TenantContextMiddleware` derives the tenant
/// from the account it finds). The whole cross-tenant defence for the three evaluators is therefore
/// the in-query <c>r.TenantId == u.TenantId</c> predicate, plus the resolver's own
/// <c>c.TenantId == tenantId</c> on the portal side. That is exactly what these tests exercise —
/// running them against a tenant-scoped context would prove nothing about identity tables.
/// </summary>
public class PortalIdentityCrossTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "Passw0rd!";

    private sealed record Harness(
        SqliteTestDbContext Db,
        Guid TenantA, Guid TenantB,
        Guid InternalA, Guid InternalB,
        Guid PortalUserA, Guid CustomerA, Guid CustomerB,
        Guid RoleB);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var hasher = new PasswordHasher();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var internalA = Guid.NewGuid();
        var internalB = Guid.NewGuid();
        var portalUserA = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var ordersViewId = Guid.NewGuid();
        var portalViewId = Guid.NewGuid();

        db.Context.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime },
            new Tenant { Id = tenantB, Name = "Bravo", Slug = "bravo", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerA, TenantId = tenantA, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = customerB, TenantId = tenantB, CustomerNumber = "KL-1", Name = "Dok NV", IsActive = true });
        db.Context.Users.AddRange(
            new User
            {
                Id = internalA, TenantId = tenantA, Email = "planner@acme.be", PasswordHash = hasher.Hash(Password),
                FirstName = "Peter", LastName = "Planner", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = internalB, TenantId = tenantB, Email = "planner@bravo.be", PasswordHash = hasher.Hash(Password),
                FirstName = "Bea", LastName = "Bravo", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = portalUserA, TenantId = tenantA, Email = "klant@haven.be", PasswordHash = hasher.Hash(Password),
                FirstName = "Kaat", LastName = "Klant", CustomerId = customerA, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });

        // Symmetric roles: same name, same granted codes, one per tenant.
        db.Context.Roles.AddRange(
            new Role { Id = roleA, TenantId = tenantA, Name = "Planner", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime },
            // Distinct NAME on purpose: the codes are symmetric so a leak gives a wrong answer,
            // but the name has to be distinguishable for the role-list assertion below.
            new Role { Id = roleB, TenantId = tenantB, Name = "Planner Bravo", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Permissions.AddRange(
            new Permission { Id = ordersViewId, Code = PermissionCodes.OrdersView, Module = "orders", Action = "view", Description = "x" },
            new Permission { Id = portalViewId, Code = PermissionCodes.CustomerPortalView, Module = "customer_portal", Action = "view", Description = "y" });
        db.Context.RolePermissions.AddRange(
            new RolePermission { RoleId = roleA, PermissionId = ordersViewId },
            new RolePermission { RoleId = roleB, PermissionId = ordersViewId },
            new RolePermission { RoleId = roleA, PermissionId = portalViewId });
        db.Context.UserRoles.AddRange(
            new UserRole { UserId = internalA, RoleId = roleA },
            new UserRole { UserId = internalB, RoleId = roleB },
            new UserRole { UserId = portalUserA, RoleId = roleA });

        db.Context.PortalAnnouncements.AddRange(
            new PortalAnnouncement { Id = Guid.NewGuid(), TenantId = tenantA, Title = "A-bericht", Body = "A", IsActive = true },
            new PortalAnnouncement { Id = Guid.NewGuid(), TenantId = tenantB, Title = "B-bericht", Body = "B", IsActive = true });
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantA, tenantB, internalA, internalB, portalUserA, customerA, customerB, roleB);
    }

    // --- 1. A tenant-B user is answered out of tenant B's grants only ---

    [Fact]
    public async Task ATenantBUser_SatisfiesOnlyWhatTenantBGrantsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new PermissionAuthorizationService(h.Db.Context);

        // Tenant B's own role grants orders.view...
        Assert.True(await sut.UserHasPermissionAsync(h.InternalB, PermissionCodes.OrdersView, CancellationToken.None));
        // ...and customer_portal.view exists in the catalog and is granted — but only by tenant A's
        // role. The tenant-B user must not pick it up from the neighbouring tenant.
        Assert.False(await sut.UserHasPermissionAsync(
            h.InternalB, PermissionCodes.CustomerPortalView, CancellationToken.None));
        Assert.Equal(
            [PermissionCodes.OrdersView],
            (await new PermissionSetService(h.Db.Context)
                .GetPermissionCodesAsync(h.InternalB, CancellationToken.None)).Order().ToList());
    }

    // --- 2. Defence in depth: a cross-tenant UserRole row grants nothing ---

    /// <summary>
    /// Arranges the shape the query filter cannot catch: a <c>UserRole</c> row (no tenant column of
    /// its own) pointing tenant A's user at tenant B's role, with tenant A's own grant of the same
    /// code stripped — so the foreign role is the ONLY remaining source of <c>orders.view</c>.
    /// </summary>
    private static async Task GiveTenantAsUserOnlyAForeignGrantAsync(Harness h)
    {
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.InternalA, RoleId = h.RoleB });
        var roleAId = h.Db.Context.Roles.Single(r => r.TenantId == h.TenantA).Id;
        var ordersViewId = h.Db.Context.Permissions.Single(p => p.Code == PermissionCodes.OrdersView).Id;
        h.Db.Context.RolePermissions.Remove(
            h.Db.Context.RolePermissions.Single(rp => rp.RoleId == roleAId && rp.PermissionId == ordersViewId));
        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task ARoleOfAnotherTenant_GrantsNothing_InEitherEvaluator()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await GiveTenantAsUserOnlyAForeignGrantAsync(h);

        Assert.False(await new PermissionAuthorizationService(h.Db.Context)
            .UserHasPermissionAsync(h.InternalA, PermissionCodes.OrdersView, CancellationToken.None));
        Assert.DoesNotContain(PermissionCodes.OrdersView,
            await new PermissionSetService(h.Db.Context).GetPermissionCodesAsync(h.InternalA, CancellationToken.None));
        // The user keeps what its OWN tenant grants — the guard is tenant-scoped, not a blanket no.
        // Also covers the RAW set that feeds the privilege comparison.
        Assert.Contains(PermissionCodes.CustomerPortalView,
            await new PermissionSetService(h.Db.Context).GetAssignedPermissionCodesAsync(h.InternalA, CancellationToken.None));
        Assert.DoesNotContain(PermissionCodes.OrdersView,
            await new PermissionSetService(h.Db.Context).GetAssignedPermissionCodesAsync(h.InternalA, CancellationToken.None));
    }

    [Fact]
    public async Task AuthService_NeverReportsARoleOrPermissionOfAnotherTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await GiveTenantAsUserOnlyAForeignGrantAsync(h);

        // AuthService runs BEFORE a tenant scope exists (login resolves the tenant from the account),
        // so the in-query r.TenantId == u.TenantId defence is the only thing standing here.
        var clock = new TestClock(Now);
        var sut = new AuthService(h.Db.Context, new PasswordHasher(), AuthTestFactory.TokenService(clock), clock);

        var me = await sut.GetCurrentUserAsync(h.InternalA, CancellationToken.None);

        Assert.NotNull(me);
        Assert.DoesNotContain(PermissionCodes.OrdersView, me!.Permissions);
        Assert.DoesNotContain("Planner Bravo", me.Roles);
        Assert.Equal(["Planner"], me.Roles);
    }

    // --- 3. The portal side: a portal identity of tenant A gets nothing from tenant B ---

    [Fact]
    public async Task PortalResolver_RefusesATenantAPortalUserUnderTenantBsScope()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        using var contextB = h.Db.CreateContextForTenant(h.TenantB);

        Assert.Null(await PortalCustomerResolver.ResolveCustomerIdAsync(
            contextB, h.TenantB, h.PortalUserA, CancellationToken.None));

        // Control: the same user resolves in its own tenant, to its own customer.
        using var contextA = h.Db.CreateContextForTenant(h.TenantA);
        Assert.Equal(h.CustomerA, await PortalCustomerResolver.ResolveCustomerIdAsync(
            contextA, h.TenantA, h.PortalUserA, CancellationToken.None));
    }

    [Fact]
    public async Task APortalUserPointedAtAnotherTenantsCustomer_ResolvesToNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Data-corruption shape: the user row survives, but its customer link crosses the tenant
        // boundary. The resolver's Customers join is tenant-scoped, so the account simply has no
        // portal context — it never lands on the foreign customer.
        h.Db.Context.Users.Single(u => u.Id == h.PortalUserA).CustomerId = h.CustomerB;
        await h.Db.Context.SaveChangesAsync();

        Assert.Null(await PortalCustomerResolver.ResolveCustomerIdAsync(
            h.Db.Context, h.TenantA, h.PortalUserA, CancellationToken.None));
        Assert.Null(await PortalCustomerResolver.ResolveCustomerIdAsync(
            h.Db.Context, h.TenantB, h.PortalUserA, CancellationToken.None));
    }

    [Fact]
    public async Task PortalEndpoint_ForATenantAUser_RefusesUnderTenantB_AndNeverLeaksItsAnnouncements()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var caller = new DevCurrentUserContext(h.PortalUserA);

        using var contextB = h.Db.CreateContextForTenant(h.TenantB);
        var tenantBService = new PortalAnnouncementService(
            contextB, new DevTenantContext(h.TenantB), caller,
            new AuditService(contextB, new DevTenantContext(h.TenantB), caller), new TestClock(Now));
        var refused = await tenantBService.ListForPortalAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, refused.Outcome);

        using var contextA = h.Db.CreateContextForTenant(h.TenantA);
        var tenantAService = new PortalAnnouncementService(
            contextA, new DevTenantContext(h.TenantA), caller,
            new AuditService(contextA, new DevTenantContext(h.TenantA), caller), new TestClock(Now));
        var allowed = await tenantAService.ListForPortalAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, allowed.Outcome);
        Assert.Equal("A-bericht", Assert.Single(allowed.Value!).Title);
    }
}
