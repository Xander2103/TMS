using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
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

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Phase 6 (corrections wave 4): real-hook notification events — order creation, portal
/// submission and the portal-review outcome (accept/reject) each publish through
/// NotificationEventService end to end (rule/catalog resolution → outbox/in-app).
/// </summary>
public class OrderNotificationEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, CustomerPortalService Portal,
        Guid TenantId, Guid CustomerId, Guid PortalUserId, Guid PlannerUserId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var plannerUserId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1, DefaultLanguage = "nl",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
            FirstName = "Marie", LastName = "Contact", Email = "marie@haven.be", IsPrimary = true, IsActive = true,
        });
        db.Context.Users.Add(new User
        {
            Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant",
            CustomerId = customerId, IsActive = true,
        });
        db.Context.Users.Add(new User
        {
            Id = plannerUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Pia", LastName = "Planner",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planning", IsActive = true });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "orders.change_status", Module = "orders", Action = "change_status" });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = plannerUserId, RoleId = roleId });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "EIGEN-1", Name = "Magazijn Haven",
            Type = LocationType.CustomerLocation, City = "Antwerpen", CustomerId = customerId, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var clock = new TestClock(Now);
        var outbox = new MessageOutboxService(db.Context, tenant, clock);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var communication = new CustomerCommunicationService(db.Context, tenant, audit);
        var events = new NotificationEventService(db.Context, tenant, outbox, notifications, communication, NullLogger<NotificationEventService>.Instance);

        var orders = new TransportOrderService(db.Context, tenant, audit, clock,
            pricingEngine: null, currentUser: null, permissionService: null, notificationEvents: events, logger: null);
        var locations = new LocationService(db.Context, tenant, audit, new CountryCodeValidator(db.Context));
        var portal = new CustomerPortalService(db.Context, tenant, new DevCurrentUserContext(portalUserId), orders, locations, audit, events);

        return new Harness(db, orders, portal, tenantId, customerId, portalUserId, plannerUserId, locationId);
    }

    private static PortalCreateOrderRequest PortalRequest(Harness h) => new(
        CustomerReference: "PO-1",
        OrderDate: new DateOnly(2026, 7, 29),
        GoodsDescription: "12 pallets",
        Remarks: null,
        Stops:
        [
            new PortalStopInput(StopType.Loading, h.LocationId, null, null, null, null, null,
                new DateTime(2026, 7, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc), null, null),
            new PortalStopInput(StopType.Unloading, null, "Klant eindbestemming", "Dorpsstraat 1", "9000", "Gent", "BE", null, null, null, null),
        ],
        CargoItems: [new PortalCargoInput("Pallets", 12, "paletten", TransportationService.Api.Modules.Packages.Entities.PackageUnitType.EuroPallet, TotalWeightKg: 7200)]);

    /// <summary>Fix round 1: order_created is fired straight from TransportOrderService.CreateAsync
    /// (not only reachable via the portal submit path) — a direct internal order create must
    /// notify orders.change_status holders too.</summary>
    [Fact]
    public async Task DirectCreate_Publishes_OrderCreated_InApp_ToChangeStatusHolders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var createRequest = new CreateTransportOrderRequest(
            h.CustomerId, "PO-2", new DateOnly(2026, 7, 29), "12 pallets",
            Quantity: null, QuantityUnit: null, WeightKg: null, VolumeM3: null, PalletCount: null,
            AdrRequired: false, CraneRequired: false, AgreedPrice: null, Notes: null,
            Stops:
            [
                new TransportOrderStopInput(StopType.Loading, h.LocationId, null, null, null, null, null,
                    new DateTime(2026, 7, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc), null, null),
                new TransportOrderStopInput(StopType.Unloading, null, "Klant eindbestemming", "Dorpsstraat 1", "9000", "Gent", "BE", null, null, null, null),
            ]);

        var created = await h.Orders.CreateAsync(createRequest, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);

        var planner = new NotificationService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(h.PlannerUserId), TimeProvider.System);
        var notified = await planner.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        var notice = Assert.Single(notified, n => n.Type == MessageKinds.OrderCreated);
        Assert.Contains(created.Order!.OrderNumber, notice.Message);
    }

    [Fact]
    public async Task PortalSubmit_Publishes_OrderSubmittedPortal_InApp_ToChangeStatusHolders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Portal.SubmitOrderAsync(PortalRequest(h), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);

        var planner = new NotificationService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(h.PlannerUserId), TimeProvider.System);
        var notified = await planner.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        Assert.Contains(notified, n => n.Type == MessageKinds.OrderSubmittedPortal);
    }

    [Fact]
    public async Task PlannerConfirmsSubmittedOrder_Publishes_OrderAccepted_EmailToPrimaryContact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var submitted = await h.Portal.SubmitOrderAsync(PortalRequest(h), CancellationToken.None);
        var orderId = submitted.Value!.Id;

        var confirmed = await h.Orders.ChangeStatusAsync(orderId, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);

        var message = Assert.Single(h.Db.Context.OutboxMessages, m => m.Kind == MessageKinds.OrderAccepted);
        Assert.Equal("marie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task PlannerSendsSubmittedOrderBackToDraft_Publishes_OrderRejected_NotOrderCreated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var submitted = await h.Portal.SubmitOrderAsync(PortalRequest(h), CancellationToken.None);
        var orderId = submitted.Value!.Id;

        await h.Orders.ChangeStatusAsync(orderId, TransportOrderStatus.Draft, CancellationToken.None);

        Assert.Contains(h.Db.Context.OutboxMessages, m => m.Kind == MessageKinds.OrderRejected);
        Assert.DoesNotContain(h.Db.Context.OutboxMessages, m => m.Kind == MessageKinds.OrderAccepted);
    }
}
