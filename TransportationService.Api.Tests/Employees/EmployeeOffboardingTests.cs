using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// MD-H6 / Security H2 (2026-10-18): HR set the employee inactive, but the linked user kept its
/// session and could log in fresh, the driver stayed plannable and the vehicle still named them.
/// Offboarding now ends access (user inactive, refresh tokens + activation links revoked, stamp
/// rotated), makes the driver unavailable, releases the vehicle slots — and keeps every trip.
/// </summary>
public class EmployeeOffboardingTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, EmployeeService Employees, Guid TenantId, Guid EmployeeId, Guid UserId, Guid DriverId, Guid VehicleId, Guid StampBefore);

    private static async Task<Harness> SeedAsync(bool userIsAdministrator = false)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = now });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-0009", FirstName = "TEST MASTER", LastName = "Chauffeur",
            IsActive = true, EmploymentStatus = EmploymentStatus.Active, CreatedAt = now, UpdatedAt = now,
        });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, EmployeeId = employeeId, Email = "test.master.chauffeur@example.test",
            FirstName = "TEST", LastName = "Chauffeur", IsActive = true, SecurityStamp = stamp,
        });
        db.Context.Set<RefreshToken>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, TokenHash = "h", CreatedAt = now, ExpiresAt = now.AddDays(7), FamilyId = Guid.NewGuid(),
        });
        db.Context.Set<UserSecurityToken>().Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Kind = UserSecurityTokenKind.Activation, TokenHash = "a",
            CreatedAt = now, ExpiresAt = now.AddDays(3),
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-0008", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0018", LicensePlate = "1-TMK-001", IsActive = true,
            FixedDriverId = driverId, CurrentDriverId = driverId,
        });
        db.Context.Trips.AddRange(
            new Trip { Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0063", TripDate = DateOnly.FromDateTime(now).AddDays(-1), Status = TripStatus.Completed, DriverId = driverId, VehicleId = vehicleId },
            new Trip { Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0099", TripDate = DateOnly.FromDateTime(now).AddDays(3), Status = TripStatus.Planned, DriverId = driverId, VehicleId = vehicleId });
        if (userIsAdministrator)
        {
            var roleId = Guid.NewGuid();
            db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true });
            db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid()));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(db.Context, tenant, new QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context), drivers, qualifications,
            new EmployeeCompletenessService(db.Context, tenant));
        return new Harness(db, employees, tenantId, employeeId, userId, driverId, vehicleId, stamp);
    }

    [Fact]
    public async Task Deactivation_EndsAccess_MakesTheDriverUnavailable_AndKeepsHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.True(await h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None));
        h.Db.Context.ChangeTracker.Clear();

        var user = await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId);
        Assert.False(user.IsActive);                                  // fresh login + refresh refused
        Assert.NotEqual(h.StampBefore, user.SecurityStamp);           // existing access tokens die
        Assert.All(await h.Db.Context.Set<RefreshToken>().ToListAsync(), t => Assert.NotNull(t.RevokedAt));
        Assert.All(await h.Db.Context.Set<UserSecurityToken>().ToListAsync(), t => Assert.NotNull(t.RevokedAt));

        Assert.False((await h.Db.Context.Drivers.SingleAsync(d => d.Id == h.DriverId)).IsActive);
        var vehicle = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        Assert.Null(vehicle.FixedDriverId);
        Assert.Null(vehicle.CurrentDriverId);

        // History is untouched: the completed trip keeps its driver; nothing was deleted.
        Assert.Equal(h.DriverId, (await h.Db.Context.Trips.SingleAsync(t => t.TripNumber == "RIT-0063")).DriverId);
        Assert.Single(await h.Db.Context.Users.Where(u => u.Id == h.UserId).ToListAsync()); // deactivated, never deleted

        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "Employee" && a.Action == "Deactivated");
        Assert.Contains("\"AccountsDeactivated\":1", audit.NewValuesJson);
        Assert.Contains("\"UpcomingTrips\":1", audit.NewValuesJson);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(), a => a.EntityType == "User" && a.EntityId == h.UserId.ToString());
    }

    [Fact]
    public async Task Reactivation_DoesNotSilentlyReEnableTheLogin()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None);

        Assert.True(await h.Employees.ReactivateAsync(h.EmployeeId, CancellationToken.None));
        h.Db.Context.ChangeTracker.Clear();

        Assert.True((await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).IsActive);
        Assert.False((await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId)).IsActive);
        Assert.False((await h.Db.Context.Drivers.SingleAsync(d => d.Id == h.DriverId)).IsActive);
    }

    [Fact]
    public async Task TheLastActiveAdministrator_IsNeverLockedOutByOffboarding()
    {
        var h = await SeedAsync(userIsAdministrator: true);
        using var _ = h.Db;

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() => h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None));
        Assert.Contains("laatste actieve beheerdersaccount", refused.Message);
        h.Db.Context.ChangeTracker.Clear();
        Assert.True((await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).IsActive);
    }

    /// <summary>UX-012: the preview names what offboarding would touch and changes nothing.</summary>
    [Fact]
    public async Task Impact_NamesPlannedTripVehicleAndAccount_AndChangesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var impact = (await h.Employees.GetOffboardingImpactAsync(h.EmployeeId, CancellationToken.None))!;

        Assert.Equal(1, impact.ActiveAccounts);
        Assert.Equal(1, impact.UpcomingTripCount);
        Assert.Equal("RIT-0099", Assert.Single(impact.UpcomingTrips).TripNumber); // the completed trip is history
        var vehicle = Assert.Single(impact.Vehicles);
        Assert.Equal(("VRT-0018", true, true), (vehicle.InternalNumber, vehicle.IsFixedDriver, vehicle.IsCurrentDriver));

        h.Db.Context.ChangeTracker.Clear();
        Assert.True((await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId)).IsActive);
        Assert.True((await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).IsActive);
        Assert.Equal(h.DriverId, (await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId)).FixedDriverId);
        Assert.Empty(await h.Db.Context.AuditLogs.Where(a => a.Action == "Deactivated").ToListAsync());

        // The preview agrees with what offboarding then reports.
        Assert.True(await h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None));
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.Action == "Deactivated");
        Assert.Contains("\"UpcomingTrips\":1", audit.NewValuesJson);

        Assert.Null(await h.Employees.GetOffboardingImpactAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Impact_OfAnEmployeeWithoutDriverRecord_IsEmpty()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var other = new Employee
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeNumber = "MED-0100", FirstName = "Leeg", LastName = "Bediende",
            IsActive = true, EmploymentStatus = EmploymentStatus.Active,
        };
        h.Db.Context.Employees.Add(other);
        await h.Db.Context.SaveChangesAsync();

        var impact = (await h.Employees.GetOffboardingImpactAsync(other.Id, CancellationToken.None))!;
        Assert.Equal((0, 0, 0), (impact.ActiveAccounts, impact.UpcomingTripCount, impact.Vehicles.Count));
    }
}
