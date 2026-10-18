using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// MD-H6 final follow-up: EVERY business path that ends an employment reaches the one offboarding
/// flow — the explicit action, an edit to Terminated / a past end date, and a future end date once
/// it has passed (sweep + request-time check). Driver-role deactivation is a separate, narrower
/// operation that releases the driver's standing slots but leaves the person's account alone.
/// </summary>
public class EmployeeOffboardingPathsTests
{
    private const string Password = "TestCore-Offboard-2026!";

    private sealed record Harness(
        SqliteTestDbContext Db, EmployeeService Employees, DriverService Drivers, DevTenantContext Tenant, AuditService Audit,
        Guid TenantId, Guid EmployeeId, Guid UserId, Guid DriverId, Guid VehicleId, Guid TrailerId, Guid StampBefore, DateOnly Today)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = now });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-0013", FirstName = "TEST CORE", LastName = "Uitdienst",
            IsActive = true, EmploymentStatus = EmploymentStatus.Active, CreatedAt = now, UpdatedAt = now,
        });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, EmployeeId = employeeId, Email = "test.core.uitdienst@example.test",
            FirstName = "TEST", LastName = "Uitdienst", IsActive = true, SecurityStamp = stamp,
            PasswordHash = new PasswordHasher().Hash(Password),
        });
        db.Context.Set<RefreshToken>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, TokenHash = "h", CreatedAt = now, ExpiresAt = now.AddDays(7), FamilyId = Guid.NewGuid(),
        });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0099", LicensePlate = "Q-TCO-001", IsActive = true });
        db.Context.Drivers.Add(new Driver
        {
            Id = driverId, TenantId = tenantId, DriverNumber = "CH-0010", EmployeeId = employeeId, IsActive = true, FixedTrailerId = trailerId,
        });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0019", LicensePlate = "1-TCO-002", IsActive = true,
            FixedDriverId = driverId, CurrentDriverId = driverId,
        });
        db.Context.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0068", TripDate = DateOnly.FromDateTime(now).AddDays(-2),
            Status = TripStatus.Completed, DriverId = driverId, VehicleId = vehicleId,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid()));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(db.Context, tenant, new QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context), drivers, qualifications,
            new EmployeeCompletenessService(db.Context, tenant));
        var today = await TenantTimeZone.TodayAsync(db.Context, tenantId, TimeProvider.System, CancellationToken.None);
        return new Harness(db, employees, drivers, tenant, audit, tenantId, employeeId, userId, driverId, vehicleId, trailerId, stamp, today);
    }

    private static UpdateEmployeeRequest Edit(EmploymentStatus status, DateOnly? endDate) =>
        new("TEST CORE", "Uitdienst", null, null, null, null, null, null, "test.core.uitdienst@example.test", null, status, endDate);

    private static async Task AssertFullyOffboardedAsync(Harness h)
    {
        h.Db.Context.ChangeTracker.Clear();
        var employee = await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId);
        Assert.False(employee.IsActive);
        Assert.Equal(EmploymentStatus.Terminated, employee.EmploymentStatus);

        var user = await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId);
        Assert.False(user.IsActive);
        Assert.NotEqual(h.StampBefore, user.SecurityStamp);
        Assert.All(await h.Db.Context.Set<RefreshToken>().ToListAsync(), t => Assert.NotNull(t.RevokedAt));

        var driver = await h.Db.Context.Drivers.SingleAsync(d => d.Id == h.DriverId);
        Assert.False(driver.IsActive);
        Assert.Null(driver.FixedTrailerId);
        var vehicle = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        Assert.Null(vehicle.FixedDriverId);
        Assert.Null(vehicle.CurrentDriverId);
        // History untouched; the trailer itself still exists and is simply free.
        Assert.Equal(h.DriverId, (await h.Db.Context.Trips.SingleAsync(t => t.TripNumber == "RIT-0068")).DriverId);
        Assert.True((await h.Db.Context.Trailers.SingleAsync(t => t.Id == h.TrailerId)).IsActive);
    }

    [Fact]
    public async Task ExplicitDeactivation_AlsoReleasesTheFixedTrailer()
    {
        using var h = await SeedAsync();

        Assert.True(await h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None));

        await AssertFullyOffboardedAsync(h);
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "Employee" && a.Action == "Deactivated");
        Assert.Contains("\"Trigger\":\"ExplicitDeactivation\"", audit.NewValuesJson);
        Assert.Contains("\"TrailersReleased\":1", audit.NewValuesJson);
    }

    [Fact]
    public async Task EditToTerminated_RunsTheSameOffboarding()
    {
        using var h = await SeedAsync();

        await h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Terminated, null), canEditConfidential: false, CancellationToken.None);

        await AssertFullyOffboardedAsync(h);
        Assert.Equal(h.Today, (await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).EmploymentEndDate);
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "Employee" && a.Action == "Deactivated");
        Assert.Contains("\"Trigger\":\"TerminatedByEdit\"", audit.NewValuesJson);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(), a => a.EntityType == "Employee" && a.Action == "Updated");
    }

    [Fact]
    public async Task EditWithAnEndDateAlreadyPassed_RunsTheSameOffboarding()
    {
        using var h = await SeedAsync();

        await h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Active, h.Today.AddDays(-3)), canEditConfidential: false, CancellationToken.None);

        await AssertFullyOffboardedAsync(h);
        Assert.Equal(h.Today.AddDays(-3), (await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).EmploymentEndDate);
    }

    [Fact]
    public async Task TerminatedWithAFutureEndDate_IsContradictory_AndRefused()
    {
        using var h = await SeedAsync();

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Terminated, h.Today.AddDays(20)), false, CancellationToken.None));
        Assert.Contains("nog niet uit dienst", refused.Message);
        h.Db.Context.ChangeTracker.Clear();
        Assert.True((await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId)).IsActive);
    }

    [Fact]
    public async Task FutureEndDate_KeepsAccessUntilTheLastWorkingDay_ThenSweepAndAccessCheckEndIt()
    {
        using var h = await SeedAsync();
        var auth = new AuthService(h.Db.Context, new PasswordHasher(), AuthTestFactory.TokenService(TimeProvider.System), TimeProvider.System);

        // Next month: nothing changes today.
        await h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Active, h.Today.AddDays(30)), false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        Assert.True((await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId)).IsActive);
        Assert.False(await EmploymentAccess.HasEndedAsync(h.Db.Context, h.TenantId, h.EmployeeId, TimeProvider.System, CancellationToken.None));
        Assert.Equal(AuthOutcome.Success, (await auth.LoginAsync("test.core.uitdienst@example.test", Password, CancellationToken.None)).Outcome);

        // The last working day is today: still in.
        await h.Db.Context.Employees.Where(e => e.Id == h.EmployeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EmploymentEndDate, h.Today));
        Assert.False(await EmploymentAccess.HasEndedAsync(h.Db.Context, h.TenantId, h.EmployeeId, TimeProvider.System, CancellationToken.None));

        // Time passes: the last working day was yesterday. Before any sweep, access is already refused.
        await h.Db.Context.Employees.Where(e => e.Id == h.EmployeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EmploymentEndDate, h.Today.AddDays(-1)));
        h.Db.Context.ChangeTracker.Clear();
        Assert.True(await EmploymentAccess.HasEndedAsync(h.Db.Context, h.TenantId, h.EmployeeId, TimeProvider.System, CancellationToken.None));
        Assert.Equal(AuthOutcome.Disabled, (await auth.LoginAsync("test.core.uitdienst@example.test", Password, CancellationToken.None)).Outcome);

        // The sweep (state-driven: a fresh worker = an app restart) runs the one offboarding flow, once.
        var sweep = new EmploymentEndSweepWorker(h.Db.Context, TimeProvider.System, NullLogger<EmploymentEndSweepWorker>.Instance);
        Assert.Equal(1, await sweep.SweepTenantAsync(h.TenantId, CancellationToken.None));
        await AssertFullyOffboardedAsync(h);
        Assert.Equal(h.Today.AddDays(-1), (await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).EmploymentEndDate);
        Assert.Contains("\"Trigger\":\"EmploymentEndDatePassed\"",
            (await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "Employee" && a.Action == "Deactivated")).NewValuesJson);
        Assert.Equal(0, await new EmploymentEndSweepWorker(h.Db.Context, TimeProvider.System, NullLogger<EmploymentEndSweepWorker>.Instance)
            .SweepTenantAsync(h.TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task PlanningPicker_MarksTheDriverUnavailable_ForTripDatesAfterTheLastWorkingDay()
    {
        using var h = await SeedAsync();
        await h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Active, h.Today.AddDays(30)), false, CancellationToken.None);

        async Task<DriverAvailabilityStatus> On(DateOnly date) =>
            (await h.Drivers.SearchAsync(null, true, null, null, null, null, null, false, false,
                TransportationService.Api.Common.Models.PageRequest.Of(1, 50), CancellationToken.None, availabilityOn: date))
            .Items.Single(d => d.Id == h.DriverId).AvailabilityStatus;

        Assert.Equal(DriverAvailabilityStatus.Available, await On(h.Today.AddDays(30)));   // the last working day
        Assert.Equal(DriverAvailabilityStatus.Unavailable, await On(h.Today.AddDays(31))); // the day after
    }

    [Fact]
    public async Task Planning_RefusesATripAfterTheLastWorkingDay_AsABlockingConflict()
    {
        using var h = await SeedAsync();
        await h.Employees.UpdateAsync(h.EmployeeId, Edit(EmploymentStatus.Active, h.Today.AddDays(30)), false, CancellationToken.None);
        var conflicts = new PlanningConflictService(h.Db.Context, h.Tenant, new QualificationStatusCalculator(), TimeProvider.System);

        var before = await conflicts.EvaluateAsync(new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripDate = h.Today.AddDays(30), DriverId = h.DriverId }, CancellationToken.None);
        Assert.DoesNotContain(before, c => c.Description.Contains("niet meer in dienst"));
        var after = await conflicts.EvaluateAsync(new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripDate = h.Today.AddDays(31), DriverId = h.DriverId }, CancellationToken.None);
        var conflict = Assert.Single(after, c => c.Description.Contains("niet meer in dienst"));
        Assert.Equal(PlanningConflictCode.DriverInactive, conflict.Code);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public async Task Sweep_HealsALegacyEndedEmploymentThatStillHadAnActiveAccount()
    {
        using var h = await SeedAsync();
        // Pre-remediation state: employee set inactive, user + driver left on.
        await h.Db.Context.Employees.Where(e => e.Id == h.EmployeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false).SetProperty(e => e.EmploymentEndDate, new DateOnly(2026, 6, 30)));
        h.Db.Context.ChangeTracker.Clear();

        var sweep = new EmploymentEndSweepWorker(h.Db.Context, TimeProvider.System, NullLogger<EmploymentEndSweepWorker>.Instance);
        Assert.Equal(1, await sweep.SweepTenantAsync(h.TenantId, CancellationToken.None));

        await AssertFullyOffboardedAsync(h);
        Assert.Equal(new DateOnly(2026, 6, 30), (await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).EmploymentEndDate);
    }

    [Fact]
    public async Task RequestTimeCheck_RefusesAnEndedEmployment_EvenWithAValidStamp_AndLeavesUnlinkedUsersAlone()
    {
        using var h = await SeedAsync();
        var adminId = Guid.NewGuid();
        var adminStamp = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = adminId, TenantId = h.TenantId, EmployeeId = null, Email = "admin@example.test", FirstName = "A", LastName = "B",
            IsActive = true, SecurityStamp = adminStamp,
        });
        await h.Db.Context.SaveChangesAsync();

        Assert.Null(await RunFilterAsync(h.Db.Context, h.UserId, h.StampBefore));
        await h.Db.Context.Employees.Where(e => e.Id == h.EmployeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EmploymentEndDate, h.Today.AddDays(-1)));

        Assert.Equal(StatusCodes.Status401Unauthorized, (await RunFilterAsync(h.Db.Context, h.UserId, h.StampBefore))?.StatusCode);
        Assert.Null(await RunFilterAsync(h.Db.Context, adminId, adminStamp));
    }

    private static async Task<ObjectResult?> RunFilterAsync(TransportationDbContext db, Guid userId, Guid stamp)
    {
        var services = new ServiceCollection().AddSingleton(db).AddSingleton(TimeProvider.System).BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(AppClaimTypes.SecurityStamp, stamp.ToString())], "test")),
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor { EndpointMetadata = [] }), []);
        await new AccountStateAuthorizationFilter().OnAuthorizationAsync(context);
        return context.Result as ObjectResult;
    }

    [Fact]
    public async Task DriverOnlyDeactivation_LeavesTheAccount_ButReleasesVehicleAndTrailerSlots()
    {
        using var h = await SeedAsync();

        var result = await h.Drivers.UpdateAsync(h.DriverId,
            new UpdateDriverRequest(null, DriverAvailabilityStatus.Available, IsActive: false, FixedTrailerId: h.TrailerId),
            CancellationToken.None);
        Assert.Equal(DriverOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();

        var driver = await h.Db.Context.Drivers.SingleAsync(d => d.Id == h.DriverId);
        Assert.False(driver.IsActive);
        Assert.Null(driver.FixedTrailerId);
        var vehicle = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        Assert.Null(vehicle.FixedDriverId);
        Assert.Null(vehicle.CurrentDriverId);

        // NOT offboarding: the employee and their login (other roles) are untouched.
        var user = await h.Db.Context.Users.SingleAsync(u => u.Id == h.UserId);
        Assert.True(user.IsActive);
        Assert.Equal(h.StampBefore, user.SecurityStamp);
        Assert.True((await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId)).IsActive);
        Assert.Null((await h.Db.Context.Set<RefreshToken>().SingleAsync()).RevokedAt);

        // Out of the driver pickers; and the inactive driver cannot be handed a vehicle slot again.
        var eligible = await h.Drivers.SearchAsync(null, null, null, null, null, null, null, false, eligibleOnly: true,
            TransportationService.Api.Common.Models.PageRequest.Of(1, 50), CancellationToken.None);
        Assert.DoesNotContain(eligible.Items, d => d.Id == h.DriverId);
        var assignments = new FleetAssignmentService(h.Db.Context, h.Tenant, h.Audit);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            assignments.SetVehicleDriverAsync(h.VehicleId, AssignmentKind.Fixed, h.DriverId, replaceExisting: true, CancellationToken.None));

        // The driver role can come back while the person is still employed.
        Assert.Equal(DriverOperationOutcome.Success, (await h.Drivers.UpdateAsync(h.DriverId,
            new UpdateDriverRequest(null, DriverAvailabilityStatus.Available, IsActive: true), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task AfterOffboarding_NeitherTheDriverNorTheAccountCanBeSwitchedBackOnAlone()
    {
        using var h = await SeedAsync();
        await h.Employees.DeactivateAsync(h.EmployeeId, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Drivers.UpdateAsync(h.DriverId,
            new UpdateDriverRequest(null, DriverAvailabilityStatus.Available, IsActive: true), CancellationToken.None));
        var users = IdentityTestFactory.UserService(h.Db, h.TenantId, Guid.NewGuid());
        var refused = await Assert.ThrowsAsync<DomainValidationException>(() => users.SetActiveAsync(h.UserId, true, CancellationToken.None));
        Assert.Contains("uit dienst", refused.Message);
    }
}
