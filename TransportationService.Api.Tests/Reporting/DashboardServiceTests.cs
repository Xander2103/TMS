using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Fleet.Services;
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
        var sut = new DashboardService(db.Context, tenant, fleet, trips, clock);
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
