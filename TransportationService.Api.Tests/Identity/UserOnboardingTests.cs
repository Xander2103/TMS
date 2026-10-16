using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

/// <summary>HR acceptance H7: an account created under Gebruikers reports that it still needs
/// activation (no password yet) until its owner chose one; the hash is never exposed.</summary>
public class UserOnboardingTests
{
    [Fact]
    public async Task CreatedUser_IsNotActivated_UntilAPasswordExists()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        db.Context.Users.Add(new User
        {
            Id = adminId, TenantId = tenantId, Email = "admin@tenant.com", FirstName = "Admin", LastName = "User",
            PasswordHash = "hash", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        var sut = IdentityTestFactory.UserService(db, tenantId, adminId);

        var created = await sut.CreateAsync(new CreateUserRequest("nieuw@tenant.com", "Nieuwe", "Gebruiker", null, null, []), CancellationToken.None);
        Assert.False(created.IsActivated);
        Assert.True(created.IsActive);

        var admin = await sut.GetByIdAsync(adminId, CancellationToken.None);
        Assert.True(admin!.IsActivated);
    }
}
