using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Reporting.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit.Abstractions;

namespace TransportationService.Api.Tests.Reporting;

/// <summary>
/// PERF-HIGH 6 (performance audit 2026-10-21): the company dashboard issued ~492 statements on
/// the 12k-trip clone — the live conflict engine once per trip of today (via the trip list) plus
/// one permission round trip per gated section. This test pins the statement count of one
/// dashboard read for an admin viewer (every section visible) with five trips today.
/// </summary>
public class DashboardQueryCountTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    /// <summary>
    /// Measured on this fixture (SQLite, 5 trips today, admin viewer): before the batching 130
    /// statements (the conflict engine once per non-completed trip, ~17 statements each, plus
    /// 7 permission round trips), after 56 — the engine now costs a fixed ~25 statements for the
    /// whole day and the viewer's section permissions are read once.
    /// </summary>
    private const int MaxStatements = 65;

    private readonly ITestOutputHelper _output;

    public DashboardQueryCountTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Get_ForAnAdminViewer_IssuesABoundedNumberOfStatements()
    {
        var counter = new QueryCountingInterceptor();
        using var db = new SqliteTestDbContext(null, counter);
        var ctx = db.Context;
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        ctx.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        ctx.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });

        Guid Driver(string key)
        {
            var employeeId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            ctx.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = $"MED-{key}", FirstName = key, LastName = "Jansen",
                EmploymentStatus = EmploymentStatus.Active, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            ctx.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, EmployeeId = employeeId, DriverNumber = $"CH-{key}", IsActive = true });
            return driverId;
        }

        Guid Vehicle(string number)
        {
            var id = Guid.NewGuid();
            ctx.Vehicles.Add(new Vehicle
            {
                Id = id, TenantId = tenantId, InternalNumber = number, LicensePlate = $"1-{number}", PayloadKg = 10000, IsActive = true,
            });
            return id;
        }

        var orderSeq = 0;
        void Trip(string number, TripStatus status, Guid? driverId, Guid? vehicleId)
        {
            var orderId = Guid.NewGuid();
            ctx.TransportOrders.Add(new TransportOrder
            {
                Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = $"ORD-{++orderSeq:000}",
                OrderDate = Today, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten", WeightKg = 500,
            });
            ctx.Trips.Add(new Trip
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = number, TripDate = Today, Status = status,
                DriverId = driverId, VehicleId = vehicleId,
                Orders = [new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, Sequence = 1 }],
            });
        }

        var d1 = Driver("d1");
        var d2 = Driver("d2");
        var d3 = Driver("d3");
        var d4 = Driver("d4");
        var v1 = Vehicle("VRT-1");
        var v2 = Vehicle("VRT-2");
        var v3 = Vehicle("VRT-3");
        var v4 = Vehicle("VRT-4");
        Trip("RIT-1", TripStatus.Planned, d1, v1);
        Trip("RIT-2", TripStatus.Planned, d2, v2);
        Trip("RIT-3", TripStatus.InProgress, d3, v3);
        Trip("RIT-4", TripStatus.Draft, null, v4);
        Trip("RIT-5", TripStatus.Completed, d4, v4);

        // Admin viewer: every permission-gated dashboard section is visible.
        var userId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "admin@acme.example", FirstName = "Ad", LastName = "Min", IsActive = true });
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Admin", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime };
        ctx.Roles.Add(role);
        ctx.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        foreach (var code in new[]
                 {
                     PermissionCodes.DashboardView, PermissionCodes.InvoicesView, PermissionCodes.EmployeeNotesView,
                     PermissionCodes.InventoryView, PermissionCodes.InventoryManage,
                     PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll,
                 })
        {
            var permission = await ctx.Permissions.FirstOrDefaultAsync(p => p.Code == code)
                             ?? ctx.Permissions.Add(new Permission { Id = Guid.NewGuid(), Code = code, Module = "test", Action = "test", Description = "test" }).Entity;
            ctx.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(ctx, tenant, new DevCurrentUserContext(null));
        var fleet = new FleetDashboardService(ctx, tenant,
            new MaintenanceService(ctx, tenant, audit, clock),
            new InspectionService(ctx, tenant, audit, clock),
            new FleetDocumentService(ctx, tenant, audit, clock,
                new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N")))),
            new DamageReportService(ctx, tenant, audit),
            new FuelService(ctx, tenant, audit));
        var trips = new TripService(ctx, tenant, audit,
            new PlanningConflictService(ctx, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(ctx, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(ctx, tenant),
            CostingTestFactory.Create(ctx, tenant, clock),
            TripPackageTestFactory.Create(ctx, tenant, clock));
        var sut = new DashboardService(ctx, tenant, fleet, trips, clock,
            new DevCurrentUserContext(userId), new PermissionAuthorizationService(ctx));
        counter.Reset();

        var dashboard = await sut.GetAsync(CancellationToken.None);

        _output.WriteLine($"dashboard (admin, {dashboard.TripsToday.Count} trips today): {counter.Count} statements");
        Assert.Equal(5, dashboard.TripsTodayTotal);
        Assert.Equal(1, dashboard.TripsTodayInProgress);
        Assert.Equal(1, dashboard.TripsTodayWithConflicts); // RIT-4 (draft without driver) is the only blocked one
        Assert.NotNull(dashboard.Inventory);
        Assert.NotNull(dashboard.Tasks);
        Assert.True(counter.Count <= MaxStatements,
            $"dashboard issued {counter.Count} statements (max {MaxStatements}):\n{string.Join("\n", counter.Statements.Select(s => s.Split('\n')[0]))}");
    }
}
