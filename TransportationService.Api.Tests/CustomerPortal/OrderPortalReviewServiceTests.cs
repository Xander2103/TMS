using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>Phase 9: internal accept/reject/request-info actions on a customer-submitted order.</summary>
public class OrderPortalReviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid OrderId, Guid StaffUserId,
        OrderPortalReviewService Sut);

    private static async Task<Harness> SeedAsync(TransportOrderStatus status = TransportOrderStatus.Submitted)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TransportationService.Api.Modules.Tenancy.Entities.TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 10, DefaultLanguage = "nl",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
            FirstName = "Marie", LastName = "Contact", Email = "marie@haven.be", IsPrimary = true, IsActive = true,
        });
        db.Context.Users.Add(new User { Id = staffUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Pia", LastName = "Planner", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-9",
            OrderDate = new DateOnly(2026, 7, 30), Status = status,
            GoodsDescription = "12 pallets",
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, LocationName = "Depot", City = "Antwerpen" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, LocationName = "Klant", City = "Gent" },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(staffUserId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var clock = new TestClock(Now);
        var outbox = new MessageOutboxService(db.Context, tenant, clock);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var communication = new CustomerCommunicationService(db.Context, tenant, audit);
        var events = new NotificationEventService(db.Context, tenant, outbox, notifications, communication, NullLogger<NotificationEventService>.Instance);

        var orders = new TransportOrderService(db.Context, tenant, audit, clock,
            pricingEngine: null, currentUser: null, permissionService: null, notificationEvents: events, logger: null);
        var messages = new CustomerMessageService(db.Context, tenant, currentUser, audit, clock, events);
        var sut = new OrderPortalReviewService(db.Context, tenant, orders, messages, audit, events, NullLogger<OrderPortalReviewService>.Instance);

        return new Harness(db, tenantId, customerId, orderId, staffUserId, sut);
    }

    [Fact]
    public async Task Accept_ConfirmsOrder_AndNotifiesCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(h.OrderId, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(TransportOrderStatus.Confirmed, result.Order!.Status);
        Assert.Contains(h.Db.Context.OutboxMessages, m => m.Kind == MessageKinds.OrderAccepted);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "TransportOrder" && a.Action == "StatusChanged");
    }

    [Fact]
    public async Task Reject_WithoutReason_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(h.OrderId, new PortalReviewRequest(PortalReviewAction.Reject, "  "), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(TransportOrderStatus.Submitted, order.Status);
    }

    [Fact]
    public async Task Reject_WithReason_CancelsOrder_StoresReason_AndNotifiesCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(
            h.OrderId, new PortalReviewRequest(PortalReviewAction.Reject, "Onvoldoende capaciteit"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(TransportOrderStatus.Cancelled, result.Order!.Status);
        Assert.Equal("Onvoldoende capaciteit", result.Order.CancellationReason);
        Assert.Contains(h.Db.Context.OutboxMessages, m => m.Kind == MessageKinds.OrderRejected);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "TransportOrder" && a.Action == "Cancelled");
    }

    [Fact]
    public async Task RequestInfo_WithoutReason_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(h.OrderId, new PortalReviewRequest(PortalReviewAction.RequestInfo, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task RequestInfo_StaysSubmitted_CreatesMessage_AndPublishesOrderInfoRequestedOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(
            h.OrderId, new PortalReviewRequest(PortalReviewAction.RequestInfo, "Wat is het exacte adres?"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(TransportOrderStatus.Submitted, result.Order!.Status);

        var message = Assert.Single(h.Db.Context.CustomerMessages);
        Assert.True(message.AuthorIsStaff);
        Assert.Equal(h.OrderId, message.TransportOrderId);
        Assert.Equal("Wat is het exacte adres?", message.Body);

        // Exactly one e-mail: the richer order_info_requested one — NOT the generic
        // customer_message_reply (would double-send for the same action otherwise).
        var outboxMessage = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal(MessageKinds.OrderInfoRequested, outboxMessage.Kind);
        Assert.Equal("marie@haven.be", outboxMessage.RecipientAddress);

        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "TransportOrder" && a.Action == "PortalInfoRequested");
    }

    [Fact]
    public async Task NonSubmittedOrder_ReturnsInvalidState()
    {
        var h = await SeedAsync(TransportOrderStatus.Confirmed);
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(h.OrderId, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task UnknownOrder_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReviewAsync(Guid.NewGuid(), new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task OtherTenantsOrder_IsInvisible_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Simulate tenant isolation: create the review service under a DIFFERENT tenant context
        // pointed at the same DbContext; the seeded order belongs to h.TenantId, not this one.
        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var currentUser = new DevCurrentUserContext(h.StaffUserId);
        var audit = new AuditService(h.Db.Context, otherTenant, currentUser);
        var clock = new TestClock(Now);
        var orders = new TransportOrderService(h.Db.Context, otherTenant, audit, clock);
        var messages = new CustomerMessageService(h.Db.Context, otherTenant, currentUser, audit, clock);
        var sut = new OrderPortalReviewService(h.Db.Context, otherTenant, orders, messages, audit);

        var result = await sut.ReviewAsync(h.OrderId, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, result.Outcome);
    }
}
