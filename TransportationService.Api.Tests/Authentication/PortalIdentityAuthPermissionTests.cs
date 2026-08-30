using System.IdentityModel.Tokens.Jwt;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Authentication;

/// <summary>
/// Fix wave B, item B1 (pass-2 finding I-2). <see cref="AuthService"/> loads the permission list
/// through its OWN query, so the identity-class guard that <c>PermissionAuthorizationService</c>
/// and <c>PermissionSetService</c> apply did not reach it: in a tenant upgraded before role
/// version 4 the legacy <c>orders.view</c> grant on the <c>klantportaal</c> role was handed to a
/// portal user in the JWT and by <c>GET /api/auth/me</c>. Nothing authorizes on that claim today,
/// but the client is told it holds internal rights it can never exercise — and a future
/// <c>hasPermission()</c> gate inside the portal shell would mis-render on it.
///
/// The rule is the single one from <c>PortalPermissionScope</c>: a user linked to a customer only
/// ever sees/holds <c>customer_portal.*</c>; an internal user is untouched.
/// </summary>
public class PortalIdentityAuthPermissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "Passw0rd!";

    private sealed record Harness(SqliteTestDbContext Db, AuthService Sut, Guid PortalUserId, Guid InternalUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var clock = new TestClock(Now);
        var hasher = new PasswordHasher();

        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var internalPermissionId = Guid.NewGuid();
        var portalPermissionId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        db.Context.Users.AddRange(
            new User
            {
                Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant",
                PasswordHash = hasher.Hash(Password), CustomerId = customerId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = internalUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Peter", LastName = "Planner",
                PasswordHash = hasher.Hash(Password), IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });

        // The legacy shape DefaultRoleUpgrades leaves behind: ONE role that carries both an
        // internal and a portal code, held by both users. The only difference is the customer link.
        db.Context.Roles.Add(new Role
        {
            Id = roleId, TenantId = tenantId, Name = "Klantportaal (legacy)", TemplateCode = "klantportaal", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Permissions.AddRange(
            new Permission { Id = internalPermissionId, Code = PermissionCodes.OrdersView, Module = "orders", Action = "view", Description = "x" },
            new Permission { Id = portalPermissionId, Code = PermissionCodes.CustomerPortalView, Module = "customer_portal", Action = "view", Description = "y" });
        db.Context.UserRoles.AddRange(
            new UserRole { UserId = portalUserId, RoleId = roleId },
            new UserRole { UserId = internalUserId, RoleId = roleId });
        db.Context.RolePermissions.AddRange(
            new RolePermission { RoleId = roleId, PermissionId = internalPermissionId },
            new RolePermission { RoleId = roleId, PermissionId = portalPermissionId });
        await db.Context.SaveChangesAsync();

        var sut = new AuthService(db.Context, hasher, AuthTestFactory.TokenService(clock), clock);
        return new Harness(db, sut, portalUserId, internalUserId);
    }

    [Fact]
    public async Task CurrentUser_ForACustomerLinkedIdentity_ReportsOnlyPortalPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var me = await h.Sut.GetCurrentUserAsync(h.PortalUserId, CancellationToken.None);

        Assert.NotNull(me);
        Assert.DoesNotContain(PermissionCodes.OrdersView, me!.Permissions);
        Assert.Contains(PermissionCodes.CustomerPortalView, me.Permissions);
        // The role itself is still reported — the guard filters permissions, not role membership.
        Assert.Contains("Klantportaal (legacy)", me.Roles);
    }

    [Fact]
    public async Task CurrentUser_ForAnInternalIdentity_IsUnaffected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var me = await h.Sut.GetCurrentUserAsync(h.InternalUserId, CancellationToken.None);

        Assert.NotNull(me);
        Assert.Contains(PermissionCodes.OrdersView, me!.Permissions);
        Assert.Contains(PermissionCodes.CustomerPortalView, me.Permissions);
    }

    [Fact]
    public async Task Login_ForACustomerLinkedIdentity_MintsATokenWithoutInternalPermissionClaims()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("klant@haven.be", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        Assert.DoesNotContain(PermissionCodes.OrdersView, result.Tokens!.User.Permissions);
        Assert.Contains(PermissionCodes.CustomerPortalView, result.Tokens.User.Permissions);

        // The claim set is the actual wire surface: assert on the decoded token, not just the DTO.
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(result.Tokens.AccessToken).Claims
            .Where(c => c.Type == "permission").Select(c => c.Value).ToList();
        Assert.DoesNotContain(PermissionCodes.OrdersView, claims);
        Assert.Contains(PermissionCodes.CustomerPortalView, claims);
    }

    [Fact]
    public async Task Login_ForAnInternalIdentity_KeepsItsInternalPermissionClaims()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("planner@acme.be", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(result.Tokens!.AccessToken).Claims
            .Where(c => c.Type == "permission").Select(c => c.Value).ToList();
        Assert.Contains(PermissionCodes.OrdersView, claims);
    }
}
