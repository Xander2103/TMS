using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Fix wave B, item B6 (pass-2 test review, I-1). <c>AccountSecurityService</c> is the single place
/// where "may this actor manage that account / grant that role" is decided, and it had no test file
/// at all — while H-14 changed the very method both sides of that comparison read.
///
/// The two sides need OPPOSITE safe directions, which is what these tests pin:
/// <list type="bullet">
/// <item><b>Target</b> — over-report. A portal account that still carries a stale internal role
/// must keep counting as holding that code, so a weaker actor cannot take it over.</item>
/// <item><b>Actor</b> — under-report. A customer-linked actor may not be authorised by a role the
/// permission evaluator refuses it; anything else lets an identity the API 403s everywhere act as
/// an administrator through this inner guard.</item>
/// </list>
/// </summary>
public class AccountSecurityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid InternalAdminId, Guid PortalActorId,
        Guid PlainUserId, Guid StaleInternalPortalUserId);

    /// <summary>
    /// One tenant, one customer and four accounts:
    /// <c>internal admin</c> (users.edit + orders.view, no customer link),
    /// <c>portal actor</c> (SAME admin role, but customer-linked — the I-1 shape),
    /// <c>plain user</c> (no roles at all),
    /// <c>stale portal user</c> (customer-linked, carries orders.view only).
    /// </summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var internalAdminId = Guid.NewGuid();
        var portalActorId = Guid.NewGuid();
        var plainUserId = Guid.NewGuid();
        var staleInternalPortalUserId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        var legacyRoleId = Guid.NewGuid();
        var usersManageId = Guid.NewGuid();
        var ordersViewId = Guid.NewGuid();
        var portalViewId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        db.Context.Users.AddRange(
            new User
            {
                Id = internalAdminId, TenantId = tenantId, Email = "admin@acme.be", PasswordHash = "x",
                FirstName = "Ad", LastName = "Min", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = portalActorId, TenantId = tenantId, Email = "klantadmin@haven.be", PasswordHash = "x",
                FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = plainUserId, TenantId = tenantId, Email = "niets@acme.be", PasswordHash = "x",
                FirstName = "Niet", LastName = "S", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = staleInternalPortalUserId, TenantId = tenantId, Email = "legacy@haven.be", PasswordHash = "x",
                FirstName = "Leg", LastName = "Acy", CustomerId = customerId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });

        db.Context.Roles.AddRange(
            new Role
            {
                Id = adminRoleId, TenantId = tenantId, Name = "Beheerder", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new Role
            {
                Id = legacyRoleId, TenantId = tenantId, Name = "Klantportaal (legacy)", TemplateCode = "klantportaal",
                IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Permissions.AddRange(
            new Permission { Id = usersManageId, Code = PermissionCodes.UsersEdit, Module = "users", Action = "edit", Description = "x" },
            new Permission { Id = ordersViewId, Code = PermissionCodes.OrdersView, Module = "orders", Action = "view", Description = "y" },
            new Permission { Id = portalViewId, Code = PermissionCodes.CustomerPortalView, Module = "customer_portal", Action = "view", Description = "z" });
        db.Context.RolePermissions.AddRange(
            new RolePermission { RoleId = adminRoleId, PermissionId = usersManageId },
            new RolePermission { RoleId = adminRoleId, PermissionId = ordersViewId },
            new RolePermission { RoleId = legacyRoleId, PermissionId = ordersViewId },
            new RolePermission { RoleId = legacyRoleId, PermissionId = portalViewId });
        db.Context.UserRoles.AddRange(
            new UserRole { UserId = internalAdminId, RoleId = adminRoleId },
            new UserRole { UserId = portalActorId, RoleId = adminRoleId },
            new UserRole { UserId = staleInternalPortalUserId, RoleId = legacyRoleId });
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantId, internalAdminId, portalActorId, plainUserId, staleInternalPortalUserId);
    }

    // --- Actor side: the three call sites the H-14 change touched ---

    [Fact]
    public async Task ActorHoldsAll_IsRefusedForACustomerLinkedActor_EvenWithTheAdminRoleAttached()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, h.PortalActorId);

        // The identical role on an internal account grants it; the customer link is the only
        // difference, and it is what the permission evaluator refuses everywhere else.
        Assert.False(await sut.ActorHoldsAllAsync([PermissionCodes.UsersEdit], CancellationToken.None));
        Assert.False(await sut.ActorHoldsAllAsync([PermissionCodes.OrdersView], CancellationToken.None));
    }

    [Fact]
    public async Task CanManageUser_IsRefusedForACustomerLinkedActor_AgainstAPeerHoldingTheSameRole()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // The sharp case: actor and target hold the IDENTICAL admin role, so before the actor side
        // was filtered the subset check was trivially satisfied and a customer-linked account could
        // reset an internal administrator's password. Note CanManageUserAsync is a *relative* guard
        // that always sits behind [RequirePermission(users.*)] — a permission-less actor against a
        // permission-less target is vacuously allowed and is not the reachable escalation.
        Assert.False(await IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, h.PortalActorId)
            .CanManageUserAsync(h.InternalAdminId, CancellationToken.None));

        // Counter-test: the identical role on an internal account still manages that peer.
        Assert.True(await IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, h.InternalAdminId)
            .CanManageUserAsync(h.InternalAdminId, CancellationToken.None));
    }

    [Fact]
    public async Task RoleAssignment_ByACustomerLinkedActor_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var adminRoleId = h.Db.Context.Roles.Single(r => r.Name == "Beheerder").Id;
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.PortalActorId);

        // The third actor-side call site: UserService's own subset guard on role assignment.
        var result = await sut.AssignRolesAsync(
            h.PlainUserId, new AssignRolesRequest([adminRoleId]), CancellationToken.None);

        Assert.NotEqual(UserOperationOutcome.Success, result.Outcome);
        Assert.Empty(h.Db.Context.UserRoles.Where(ur => ur.UserId == h.PlainUserId).ToList());
    }

    // --- Actor side: the internal counter-tests, so the guard cannot be "refuse everything" ---

    [Fact]
    public async Task InternalActor_KeepsItsFullEffectiveSet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, h.InternalAdminId);

        Assert.True(await sut.ActorHoldsAllAsync(
            [PermissionCodes.UsersEdit, PermissionCodes.OrdersView], CancellationToken.None));
        Assert.True(await sut.CanManageUserAsync(h.PlainUserId, CancellationToken.None));
    }

    [Fact]
    public async Task EscalationGuard_StillRefusesGrantingACodeTheActorLacks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, h.InternalAdminId);

        // invoices.view is in the catalog but on nobody's role here: an internal admin still may
        // not hand out what it does not itself hold.
        Assert.False(await sut.ActorHoldsAllAsync([PermissionCodes.InvoicesView], CancellationToken.None));
        Assert.False(await sut.ActorHoldsAllAsync(
            [PermissionCodes.UsersEdit, PermissionCodes.InvoicesView], CancellationToken.None));
    }

    // --- Target side: the opposite direction, which must NOT be narrowed ---

    [Fact]
    public async Task TargetSide_StillCountsAStaleInternalRoleOnAPortalAccount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // The stale portal account cannot USE orders.view (PortalIdentityClassGuardTests pins
        // that), but it must still be REPORTED as holding it: an actor lacking orders.view may not
        // reset its password or rewrite its roles.
        var reported = await IdentityTestFactory
            .AccountSecurity(h.Db, h.TenantId, h.InternalAdminId)
            .EffectivePermissionsAsync(h.StaleInternalPortalUserId, CancellationToken.None);
        Assert.Contains(PermissionCodes.OrdersView, reported);
        Assert.Contains(PermissionCodes.CustomerPortalView, reported);
    }

    [Fact]
    public async Task ActorWithoutTheTargetsCodes_CannotManageAStalePortalAccount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // A second internal actor holding ONLY users.edit — not customer_portal.view.
        var weakActorId = Guid.NewGuid();
        var weakRoleId = Guid.NewGuid();
        var usersManageId = h.Db.Context.Permissions.Single(p => p.Code == PermissionCodes.UsersEdit).Id;
        h.Db.Context.Users.Add(new User
        {
            Id = weakActorId, TenantId = h.TenantId, Email = "zwak@acme.be", PasswordHash = "x",
            FirstName = "Zwak", LastName = "Ke", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Roles.Add(new Role
        {
            Id = weakRoleId, TenantId = h.TenantId, Name = "Gebruikersbeheer", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.RolePermissions.Add(new RolePermission { RoleId = weakRoleId, PermissionId = usersManageId });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = weakActorId, RoleId = weakRoleId });
        await h.Db.Context.SaveChangesAsync();

        var sut = IdentityTestFactory.AccountSecurity(h.Db, h.TenantId, weakActorId);

        Assert.False(await sut.CanManageUserAsync(h.StaleInternalPortalUserId, CancellationToken.None));
    }
}
