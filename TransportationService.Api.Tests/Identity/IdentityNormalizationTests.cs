using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// MD-H11 (master-data acceptance 2026-10-18): "TEST.CORE.USER@example.test" became a second account
/// next to "test.core.user@example.test", and logging in with a capitalised address failed.
/// MD-M7: renaming a role to an existing name was a 500 and "planner" could coexist with "Planner".
/// </summary>
public class IdentityNormalizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private const string Password = "TestCore-2026!";

    private static async Task<(SqliteTestDbContext Db, Guid TenantId, Guid AdminId)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = adminId, TenantId = tenantId, Email = "test.core.user@example.test", FirstName = "Test", LastName = "Core",
            PasswordHash = new PasswordHasher().Hash(Password), IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Planner", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        return (db, tenantId, adminId);
    }

    [Fact]
    public async Task ACaseVariantOfAnExistingEmail_IsRefused_AndTheIndexIsCaseInsensitive()
    {
        var (db, tenantId, adminId) = await SeedAsync();
        using var _ = db;
        var users = IdentityTestFactory.UserService(db, tenantId, adminId);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            users.CreateAsync(new CreateUserRequest(" TEST.CORE.USER@example.test ", "Dubbel", "Account", null, null, []), CancellationToken.None));
        Assert.Contains("bestaat al een gebruiker", refused.Message);

        db.Context.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenantId, Email = "Test.Core.User@Example.test", FirstName = "x", LastName = "y" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Login_WithAMixedCaseAddress_FindsTheExistingAccount_TheTypedFormIsKept()
    {
        var (db, _, adminId) = await SeedAsync();
        using var __ = db;
        var auth = new AuthService(db.Context, new PasswordHasher(), AuthTestFactory.TokenService(new TestClock(Now)), new TestClock(Now));

        var result = await auth.LoginAsync("  TEST.Core.User@EXAMPLE.test", Password, CancellationToken.None);

        Assert.NotNull(result.Tokens);
        Assert.Equal("test.core.user@example.test", (await db.Context.Users.SingleAsync(u => u.Id == adminId)).Email);
    }

    [Fact]
    public async Task RoleNames_AreUniqueRegardlessOfCase_AndARenameCollisionIsAValidationError()
    {
        var (db, tenantId, adminId) = await SeedAsync();
        using var _ = db;
        var roles = IdentityTestFactory.RoleService(db, tenantId, adminId);

        var duplicate = await Assert.ThrowsAsync<DomainValidationException>(() =>
            roles.CreateAsync(new CreateRoleRequest("planner", null), CancellationToken.None));
        Assert.Contains("bestaat al een rol", duplicate.Message);

        var test = await roles.CreateAsync(new CreateRoleRequest("TEST rol", null), CancellationToken.None);
        var rename = await roles.UpdateAsync(test.Id, new UpdateRoleRequest("PLANNER", null), CancellationToken.None);
        Assert.Equal(RoleOperationOutcome.ValidationFailed, rename.Outcome);
        Assert.Contains("bestaat al een rol", rename.Error);
    }
}
