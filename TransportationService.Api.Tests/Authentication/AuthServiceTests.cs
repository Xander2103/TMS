using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Authentication;

public class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "Passw0rd!";

    private sealed record Harness(
        SqliteTestDbContext Db, AuthService Sut, TestClock Clock, Guid TenantId, Guid UserId);

    private static async Task<Harness> SeedAsync(
        bool userActive = true, bool userBlocked = false, bool tenantActive = true, bool withPassword = true)
    {
        var db = new SqliteTestDbContext();
        var clock = new TestClock(Now);
        var hasher = new PasswordHasher();
        var tokens = AuthTestFactory.TokenService(clock);

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = tenantActive, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "user@acme.com", FirstName = "Uma", LastName = "Ng",
            PasswordHash = withPassword ? hasher.Hash(Password) : null,
            IsActive = userActive, IsBlocked = userBlocked, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Permissions.Add(new Permission { Id = permId, Code = "employees.view", Module = "employees", Action = "view", Description = "x" });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permId });
        await db.Context.SaveChangesAsync();

        var sut = new AuthService(db.Context, hasher, tokens, clock);
        return new Harness(db, sut, clock, tenantId, userId);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokensRolesAndPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        Assert.NotNull(result.Tokens);
        Assert.False(string.IsNullOrWhiteSpace(result.Tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Tokens.RefreshToken));
        Assert.Contains("Planner", result.Tokens.User.Roles);
        Assert.Contains("employees.view", result.Tokens.User.Permissions);
        Assert.Equal("Acme", result.Tokens.User.TenantName);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsInvalidCredentials()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("nobody@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsInvalidCredentials()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", "wrong", CancellationToken.None);

        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task Login_InactiveUser_IsRejected()
    {
        var h = await SeedAsync(userActive: false);
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Login_BlockedUser_IsRejected()
    {
        var h = await SeedAsync(userBlocked: true);
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Login_InactiveTenant_IsRejected()
    {
        var h = await SeedAsync(tenantActive: false);
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task Login_UserWithoutPassword_CannotAuthenticate()
    {
        var h = await SeedAsync(withPassword: false);
        using var _ = h.Db;

        var result = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldTokenNoLongerValid()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var login = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);
        var originalRefresh = login.Tokens!.RefreshToken;

        var refreshed = await h.Sut.RefreshAsync(originalRefresh, CancellationToken.None);
        Assert.Equal(AuthOutcome.Success, refreshed.Outcome);
        Assert.NotEqual(originalRefresh, refreshed.Tokens!.RefreshToken);

        // Old token is now revoked (rotation) and must be rejected.
        var reuse = await h.Sut.RefreshAsync(originalRefresh, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, reuse.Outcome);
    }

    [Fact]
    public async Task Refresh_AfterExpiry_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var login = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        h.Clock.Advance(TimeSpan.FromDays(15)); // default refresh lifetime is 14 days

        var refreshed = await h.Sut.RefreshAsync(login.Tokens!.RefreshToken, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, refreshed.Outcome);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var login = await h.Sut.LoginAsync("user@acme.com", Password, CancellationToken.None);

        await h.Sut.LogoutAsync(login.Tokens!.RefreshToken, CancellationToken.None);

        var refreshed = await h.Sut.RefreshAsync(login.Tokens.RefreshToken, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, refreshed.Outcome);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsProfileWithRolesAndPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var me = await h.Sut.GetCurrentUserAsync(h.UserId, CancellationToken.None);

        Assert.NotNull(me);
        Assert.Equal("user@acme.com", me!.Email);
        Assert.Contains("Planner", me.Roles);
        Assert.Contains("employees.view", me.Permissions);
    }

    [Fact]
    public async Task LoadRolesAndPermissions_DoesNotLeakOtherTenantRoles()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Assign the user a role that belongs to a DIFFERENT tenant — it must be ignored.
        var foreignRoleId = Guid.NewGuid();
        var foreignPermId = Guid.NewGuid();
        h.Db.Context.Roles.Add(new Role { Id = foreignRoleId, TenantId = Guid.NewGuid(), Name = "ForeignAdmin", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Permissions.Add(new Permission { Id = foreignPermId, Code = "secret.manage", Module = "secret", Action = "manage", Description = "x" });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.UserId, RoleId = foreignRoleId });
        h.Db.Context.RolePermissions.Add(new RolePermission { RoleId = foreignRoleId, PermissionId = foreignPermId });
        await h.Db.Context.SaveChangesAsync();

        var me = await h.Sut.GetCurrentUserAsync(h.UserId, CancellationToken.None);

        Assert.DoesNotContain("ForeignAdmin", me!.Roles);
        Assert.DoesNotContain("secret.manage", me.Permissions);
    }
}
