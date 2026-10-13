using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class PortalDashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dashboard_CountsMatchSeededScenario()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Users.Add(new User { Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true });
        db.Context.Users.Add(new User { Id = staffUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Pia", LastName = "Planner", IsActive = true });

        var activeOrderId = Guid.NewGuid();
        var confirmedOrderId = Guid.NewGuid();
        var completedOrderId = Guid.NewGuid();
        var cancelledOrderId = Guid.NewGuid();
        db.Context.TransportOrders.AddRange(
            new TransportOrder { Id = activeOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1", OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Submitted },
            new TransportOrder { Id = confirmedOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-2", OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Confirmed },
            new TransportOrder { Id = completedOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-3", OrderDate = new DateOnly(2026, 7, 25), Status = TransportOrderStatus.Completed },
            new TransportOrder { Id = cancelledOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-4", OrderDate = new DateOnly(2026, 7, 25), Status = TransportOrderStatus.Cancelled });

        // Upcoming delivery within 7 days on the confirmed order.
        db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = confirmedOrderId, Sequence = 2,
            StopType = StopType.Unloading, City = "Gent", ConfirmedFrom = Now.UtcDateTime.AddDays(3),
        });
        // Outside the 7-day window: excluded.
        db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = activeOrderId, Sequence = 2,
            StopType = StopType.Unloading, City = "Brugge", ConfirmedFrom = Now.UtcDateTime.AddDays(20),
        });

        // ExecutionException.TripId is a real FK — a trip row must exist.
        var tripId = Guid.NewGuid();
        db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "TR-1", TripDate = new DateOnly(2026, 7, 30),
        });

        // One open, customer-visible exception -> counts; one resolved and one internal-only don't.
        db.Context.ExecutionExceptions.Add(new ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = activeOrderId,
            Type = ExecutionExceptionType.Delay, Status = ExecutionExceptionStatus.Open, CustomerVisible = true,
            Description = "Vertraging", OccurredAt = Now.UtcDateTime,
        });
        db.Context.ExecutionExceptions.Add(new ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = confirmedOrderId,
            Type = ExecutionExceptionType.Delay, Status = ExecutionExceptionStatus.Resolved, CustomerVisible = true,
            Description = "Opgelost", OccurredAt = Now.UtcDateTime,
        });
        db.Context.ExecutionExceptions.Add(new ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = confirmedOrderId,
            Type = ExecutionExceptionType.Other, Status = ExecutionExceptionStatus.Open, CustomerVisible = false,
            Description = "Intern", OccurredAt = Now.UtcDateTime,
        });

        // 2 non-Draft invoices + 1 Draft (excluded).
        void AddInvoice(Guid id, string number, InvoiceStatus status, DateOnly date)
        {
            db.Context.Invoices.Add(new Invoice
            {
                Id = id, TenantId = tenantId, CustomerId = customerId, InvoiceNumber = number,
                InvoicePeriodYear = date.Year, InvoicePeriodMonth = date.Month, InvoiceDate = date,
                DueDate = date.AddDays(30), Status = status, Currency = "EUR",
            });
        }

        AddInvoice(Guid.NewGuid(), "2026070001", InvoiceStatus.Sent, new DateOnly(2026, 7, 20));
        AddInvoice(Guid.NewGuid(), "2026070002", InvoiceStatus.Paid, new DateOnly(2026, 7, 25));
        AddInvoice(Guid.NewGuid(), "2026070003", InvoiceStatus.Draft, new DateOnly(2026, 7, 28));

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var messageService = new CustomerMessageService(db.Context, tenant, new DevCurrentUserContext(portalUserId), audit, clock);
        // One unread staff message for the portal user.
        await messageService.SendToCustomerAsync(customerId, new SendCustomerMessageRequest(null, "Antwoord van staf"), CancellationToken.None);
        var staffSideSend = new CustomerMessageService(db.Context, tenant, new DevCurrentUserContext(staffUserId), audit, clock);
        await staffSideSend.SendToCustomerAsync(customerId, new SendCustomerMessageRequest(null, "Nog een antwoord"), CancellationToken.None);

        var invoiceService = new InvoiceService(db.Context, tenant, audit, clock,
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new AccountingService(db.Context, tenant, audit));
        var announcementService = new PortalAnnouncementService(db.Context, tenant, new DevCurrentUserContext(portalUserId), audit, clock);
        await announcementService.CreateAsync(new SavePortalAnnouncementRequest("Onderhoud gepland", "Body", null, null, true), CancellationToken.None);

        var dashboardMessageService = new CustomerMessageService(db.Context, tenant, new DevCurrentUserContext(portalUserId), audit, clock);
        var sut = new PortalDashboardService(
            db.Context, tenant, new DevCurrentUserContext(portalUserId), dashboardMessageService, invoiceService, announcementService, clock);

        var result = await sut.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        var dto = result.Value!;

        Assert.Equal(2, dto.ActiveOrders); // Submitted + Confirmed, not Completed/Cancelled
        Assert.Single(dto.UpcomingDeliveries);
        Assert.Equal("ORD-2", dto.UpcomingDeliveries[0].OrderNumber);
        Assert.Equal(1, dto.ProblemOrders); // only the open + CustomerVisible one's order counts
        Assert.Equal(2, dto.UnreadMessages); // 2 staff messages, none marked read yet
        Assert.Equal(2, dto.RecentInvoices.Count); // Draft excluded
        Assert.Single(dto.Announcements);
    }

    [Fact]
    public async Task Dashboard_UnlinkedUser_ReturnsNoCustomerLink()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User { Id = staffUserId, TenantId = tenantId, Email = "staff@acme.be", FirstName = "S", LastName = "T", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var messageService = new CustomerMessageService(db.Context, tenant, new DevCurrentUserContext(staffUserId), audit, clock);
        var invoiceService = new InvoiceService(db.Context, tenant, audit, clock,
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new AccountingService(db.Context, tenant, audit));
        var announcementService = new PortalAnnouncementService(db.Context, tenant, new DevCurrentUserContext(staffUserId), audit, clock);
        var sut = new PortalDashboardService(
            db.Context, tenant, new DevCurrentUserContext(staffUserId), messageService, invoiceService, announcementService, clock);

        var result = await sut.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, result.Outcome);
    }
}
