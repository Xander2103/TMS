using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// PERF-H2 (performance/concurrency final remediation 2026-10-21): parallel replacements of one
/// user's role set collided on <c>PK_user_roles</c> with a raw 500. On PostgreSQL the user row is
/// now locked before the existing roles are read (replacements serialize, every caller gets 200);
/// on every provider a collision that still reaches the index maps to 409 with the reload sentence.
/// </summary>
public class RoleAssignmentRaceTests
{
    private static UserService UserService(TransportationDbContext db, Guid tenantId, Guid actorId)
    {
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(actorId);
        var audit = new AuditService(db, tenant, currentUser);
        var accountSecurity = new AccountSecurityService(db, tenant, currentUser, new PermissionSetService(db), TimeProvider.System);
        return new UserService(db, tenant, audit, new PasswordHasher(), currentUser, accountSecurity, PasswordPolicy.Default);
    }

    [Fact]
    public async Task TwoReplacementsOfTheSameRoleSet_TheLoserMapsTo409_AndOneRoleRowSurvives()
    {
        var race = new CompetingWriterInterceptor();
        using var db = new SqliteTestDbContext(null, race);
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        var plannerRoleId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Context.Roles.AddRange(
            new Role { Id = adminRoleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Role { Id = plannerRoleId, TenantId = tenantId, Name = "Planner", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Context.Users.AddRange(
            new User { Id = actorId, TenantId = tenantId, Email = "admin@acme.be", FirstName = "Ann", LastName = "Admin", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new User { Id = targetId, TenantId = tenantId, Email = "piet@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Context.UserRoles.Add(new UserRole { UserId = actorId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // Admin B hands out the same role between A's read of the existing set and A's insert.
        race.Arm(
            context => context.ChangeTracker.Entries<UserRole>().Any(e => e.State == EntityState.Added && e.Entity.UserId == targetId),
            async cancellationToken =>
            {
                await using var other = db.CreateContextForTenant(tenantId);
                var winner = await UserService(other, tenantId, actorId).AssignRolesAsync(targetId, new AssignRolesRequest([plannerRoleId]), cancellationToken);
                Assert.Equal(UserOperationOutcome.Success, winner.Outcome);
            });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            UserService(db.Context, tenantId, actorId).AssignRolesAsync(targetId, new AssignRolesRequest([plannerRoleId]), CancellationToken.None));

        Assert.Equal(1, race.Fired);
        Assert.Equal("PK_user_roles", UniqueViolations.ConstraintName(exception));
        Assert.True(KnownUniqueConflictExceptionFilter.TryMap(exception, out var message));
        Assert.Equal("De rollen van deze gebruiker zijn intussen door iemand anders gewijzigd. Herlaad en probeer opnieuw.", message);

        var roles = await db.Context.UserRoles.AsNoTracking().Where(ur => ur.UserId == targetId).ToListAsync();
        Assert.Equal(plannerRoleId, Assert.Single(roles).RoleId);
        // One "AssignRoles" audit row: the loser wrote nothing.
        Assert.Single(await db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "User" && a.EntityId == targetId.ToString() && a.Action == "AssignRoles").ToListAsync());
    }
}
