using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Reporting.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reporting;

public class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, DashboardService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var fleet = new FleetDashboardService(db.Context, tenant,
            new MaintenanceService(db.Context, tenant, audit, clock),
            new InspectionService(db.Context, tenant, audit, clock),
            new FleetDocumentService(db.Context, tenant, audit, clock, new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ts-tests", System.Guid.NewGuid().ToString("N")))),
            new DamageReportService(db.Context, tenant, audit),
            new FuelService(db.Context, tenant, audit));
        var trips = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var sut = new DashboardService(db.Context, tenant, fleet, trips, clock,
            new DevCurrentUserContext(null), new PermissionAuthorizationService(db.Context));
        return new Harness(db, sut, tenantId, customerId);
    }

    private static TransportOrder Order(Guid tenantId, Guid customerId, string number, TransportOrderStatus status, DateOnly date) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
        OrderNumber = number, OrderDate = date, Status = status, GoodsDescription = "x",
    };

    [Fact]
    public async Task Get_AggregatesOrderCounts_RevenueAndOutstanding()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        h.Db.Context.TransportOrders.AddRange(
            Order(h.TenantId, h.CustomerId, "ORD-1", TransportOrderStatus.Draft, new(2026, 7, 10)),
            Order(h.TenantId, h.CustomerId, "ORD-2", TransportOrderStatus.Confirmed, new(2026, 7, 11)),
            Order(h.TenantId, h.CustomerId, "ORD-3", TransportOrderStatus.InProgress, new(2026, 7, 12)),
            Order(h.TenantId, h.CustomerId, "ORD-4", TransportOrderStatus.Completed, new(2026, 7, 13)));

        // Paid invoice this month (revenue) + sent overdue invoice (outstanding).
        var paid = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            InvoiceNumber = "FAC-1", InvoiceDate = new(2026, 7, 5), DueDate = new(2026, 8, 4), Status = InvoiceStatus.Paid,
        };
        var sent = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            InvoiceNumber = "FAC-2", InvoiceDate = new(2026, 6, 1), DueDate = new(2026, 7, 1), Status = InvoiceStatus.Sent,
        };
        h.Db.Context.Invoices.AddRange(paid, sent);
        h.Db.Context.InvoiceLines.AddRange(
            new InvoiceLine { Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = paid.Id, Sequence = 1, Description = "a", Quantity = 1, UnitPrice = 1000m, VatRatePercent = 21m },
            new InvoiceLine { Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = sent.Id, Sequence = 1, Description = "b", Quantity = 1, UnitPrice = 500m, VatRatePercent = 21m });
        await h.Db.Context.SaveChangesAsync();

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(2, dashboard.OrdersOpenCount);
        Assert.Equal(1, dashboard.OrdersInExecutionCount);
        Assert.Equal(1, dashboard.OrdersCompletedThisMonth);
        Assert.Equal(1210m, dashboard.RevenueInvoicedThisMonth);
        Assert.Equal(605m, dashboard.OutstandingAmount);
        Assert.Equal(1, dashboard.OverdueInvoiceCount); // vervaldag 1 juli < vandaag 18 juli
        Assert.Equal(4, dashboard.RecentOrders.Count);
        Assert.Equal("ORD-4", dashboard.RecentOrders[0].OrderNumber);
    }

    private async Task<Guid> SeedUserWithPermissionAsync(Harness h, params string[] permissionCodes)
    {
        var userId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = userId, TenantId = h.TenantId, Email = $"{userId}@acme.example", FirstName = "Ann", LastName = "HR", IsActive = true,
        });
        var role = new Role
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Name = "Testrol", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        };
        h.Db.Context.Roles.Add(role);
        h.Db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        foreach (var code in permissionCodes)
        {
            var permission = await h.Db.Context.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (permission is null)
            {
                permission = new Permission { Id = Guid.NewGuid(), Code = code, Module = "test", Action = "test", Description = "test" };
                h.Db.Context.Permissions.Add(permission);
            }
            h.Db.Context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }
        await h.Db.Context.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Get_WithEmployeeNotesViewPermission_IncludesPinnedNotes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employeeId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+3231112233",
            Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
            EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = TransportationService.Api.Modules.Employees.Entities.EmploymentStatus.Active, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(h.TenantId);
        var notes = new EmployeeNoteService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));
        var note = await notes.CreateAsync(employeeId, "Heeft hoogtevrees — nooit op kraanwerk.", CancellationToken.None);
        await notes.SetPinnedAsync(employeeId, note!.Id, true, CancellationToken.None);

        var userId = await SeedUserWithPermissionAsync(h, PermissionCodes.DashboardView, PermissionCodes.EmployeeNotesView);
        var dashboard = await BuildWithUser(h, userId).GetAsync(CancellationToken.None);

        var pinned = Assert.Single(dashboard.PinnedEmployeeNotes);
        Assert.Equal(note.Id, pinned.NoteId);
        Assert.Equal(employeeId, pinned.EmployeeId);
        Assert.Equal("Jan Janssen", pinned.EmployeeName);
        Assert.Equal("Heeft hoogtevrees — nooit op kraanwerk.", pinned.Excerpt);
    }

    [Fact]
    public async Task Get_WithoutEmployeeNotesViewPermission_OmitsPinnedNotes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employeeId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+3231112233",
            Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
            EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = TransportationService.Api.Modules.Employees.Entities.EmploymentStatus.Active, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(h.TenantId);
        var notes = new EmployeeNoteService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));
        var note = await notes.CreateAsync(employeeId, "Vertrouwelijk.", CancellationToken.None);
        await notes.SetPinnedAsync(employeeId, note!.Id, true, CancellationToken.None);

        // A user with dashboard.view but NOT employee_notes.view never sees the note.
        var withoutNotesPermission = await SeedUserWithPermissionAsync(h, PermissionCodes.DashboardView);
        var dashboard = await BuildWithUser(h, withoutNotesPermission).GetAsync(CancellationToken.None);
        Assert.Empty(dashboard.PinnedEmployeeNotes);

        // No authenticated user at all → also empty, never throws.
        var anonymous = await BuildWithUser(h, null).GetAsync(CancellationToken.None);
        Assert.Empty(anonymous.PinnedEmployeeNotes);
    }

    private static DashboardService BuildWithUser(Harness h, Guid? userId)
    {
        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var fleet = new FleetDashboardService(h.Db.Context, tenant,
            new TransportationService.Api.Modules.Fleet.Services.MaintenanceService(h.Db.Context, tenant, audit, clock),
            new TransportationService.Api.Modules.Fleet.Services.InspectionService(h.Db.Context, tenant, audit, clock),
            new TransportationService.Api.Modules.Fleet.Services.FleetDocumentService(h.Db.Context, tenant, audit, clock, new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N")))),
            new TransportationService.Api.Modules.Fleet.Services.DamageReportService(h.Db.Context, tenant, audit),
            new TransportationService.Api.Modules.Fleet.Services.FuelService(h.Db.Context, tenant, audit));
        var trips = new TripService(h.Db.Context, tenant, audit,
            new PlanningConflictService(h.Db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(h.Db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(h.Db.Context, tenant),
            CostingTestFactory.Create(h.Db.Context, tenant, clock),
            TripPackageTestFactory.Create(h.Db.Context, tenant, clock));
        return new DashboardService(h.Db.Context, tenant, fleet, trips, clock,
            new DevCurrentUserContext(userId), new PermissionAuthorizationService(h.Db.Context));
    }

    [Fact]
    public async Task Get_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = Guid.NewGuid();
        var foreignCustomer = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Customers.Add(new Customer { Id = foreignCustomer, TenantId = foreignTenant, CustomerNumber = "X", Name = "Spy", IsActive = true });
        h.Db.Context.TransportOrders.Add(Order(foreignTenant, foreignCustomer, "ORD-X", TransportOrderStatus.Draft, new(2026, 7, 10)));
        await h.Db.Context.SaveChangesAsync();

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(0, dashboard.OrdersOpenCount);
        Assert.Empty(dashboard.RecentOrders);
    }
}
