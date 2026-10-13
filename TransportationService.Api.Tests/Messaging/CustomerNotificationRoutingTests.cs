using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// End-to-end: a box ticked on the contact card ("Ontvangt meldingen") decides who receives a
/// customer-facing order event; the primary contact is only the fallback when nobody is
/// subscribed. Credit notes route to the CreditNote rule, not the Invoice rule.
/// </summary>
public class CustomerNotificationRoutingTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, NotificationEventService Events, CustomerContactSubscriptionService Subscriptions,
        Guid TenantId, Guid CustomerId, Guid PrimaryContactId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var primaryId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, DefaultLanguage = "nl" });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Email = "algemeen@haven.be", IsActive = true,
        });
        db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = primaryId, TenantId = tenantId, CustomerId = customerId,
            FirstName = "Marie", LastName = "Primair", Email = "marie@haven.be", IsPrimary = true, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var outbox = new MessageOutboxService(db.Context, tenant, clock);
        var communication = new CustomerCommunicationService(db.Context, tenant, audit);
        var events = new NotificationEventService(db.Context, tenant, outbox, notifications, communication,
            NullLogger<NotificationEventService>.Instance);
        var subscriptions = new CustomerContactSubscriptionService(db.Context, tenant, audit);

        return new Harness(db, events, subscriptions, tenantId, customerId, primaryId);
    }

    private static async Task<Guid> AddContactAsync(Harness h, string first, string email)
    {
        var contact = new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            FirstName = first, LastName = "Contact", Email = email, IsActive = true,
        };
        h.Db.Context.CustomerContacts.Add(contact);
        await h.Db.Context.SaveChangesAsync();
        return contact.Id;
    }

    private static NotificationEventContext OrderContext(Guid customerId) => new(
        "TransportOrder", Guid.NewGuid().ToString(),
        new Dictionary<string, string> { ["orderNumber"] = "ORD-1", ["customerName"] = "Haven BV", ["goodsDescription"] = "Pallets" })
    { CustomerId = customerId };

    [Fact]
    public async Task PlanningBoxTicked_RoutesPlanningWindowMailToThatContact_NotThePrimaryContact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "jan@haven.be");
        await h.Subscriptions.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);

        await h.Events.PublishAsync(MessageKinds.OrderPickupWindow, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("jan@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task NobodySubscribed_FallsBackToThePrimaryContact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Events.PublishAsync(MessageKinds.OrderPickupWindow, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("marie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task UntickedBox_StopsTheMail_AndThePrimaryContactTakesOverAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "jan@haven.be");
        await h.Subscriptions.SetForContactAsync(h.CustomerId, jan, ["delivery-pod"], CancellationToken.None);
        await h.Subscriptions.SetForContactAsync(h.CustomerId, jan, [], CancellationToken.None);

        await h.Events.PublishAsync(MessageKinds.OrderPodAvailable, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("marie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task SubscribedPrimaryContact_IsMailedOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Subscriptions.SetForContactAsync(h.CustomerId, h.PrimaryContactId, ["order-confirmation"], CancellationToken.None);

        await h.Events.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("marie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public void EveryCustomerFacingOrderEvent_ResolvesThroughACommunicationRule_WithPrimaryFallback()
    {
        foreach (var kind in new[]
        {
            MessageKinds.OrderAccepted, MessageKinds.OrderRejected, MessageKinds.OrderInfoRequested,
            MessageKinds.OrderPickupWindow, MessageKinds.OrderDeliveryWindow,
            MessageKinds.OrderPickupCompleted, MessageKinds.OrderDeliveryCompleted, MessageKinds.OrderDelayDetected,
            MessageKinds.OrderFailedDelivery, MessageKinds.OrderDamageRegistered, MessageKinds.OrderPodAvailable,
        })
        {
            var specs = NotificationEventCatalog.Resolve(kind)!.DefaultRecipients;
            var ruleIndex = specs.ToList().FindIndex(s => s.Type == NotificationRecipientType.CustomerCommunicationRule);
            var fallbackIndex = specs.ToList().FindIndex(s => s.Type == NotificationRecipientType.CustomerPrimaryContact);
            Assert.True(ruleIndex >= 0, $"{kind} has no CustomerCommunicationRule spec");
            Assert.True(fallbackIndex > ruleIndex, $"{kind}: primary-contact fallback must come after the rule");
            Assert.Contains(Enum.Parse<CustomerCommunicationType>(specs[ruleIndex].Value!), CustomerNotificationCatalog.CoveredTypes);
        }
    }

    [Fact]
    public async Task CreditNote_RoutesToTheCreditNoteRule_NotTheInvoiceRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var invoiceContact = await AddContactAsync(h, "Boek", "facturatie@haven.be");
        var creditContact = await AddContactAsync(h, "Credit", "credit@haven.be");
        await h.Subscriptions.SetForContactAsync(h.CustomerId, invoiceContact, ["invoice"], CancellationToken.None);
        await h.Subscriptions.SetForContactAsync(h.CustomerId, creditContact, ["credit-note"], CancellationToken.None);

        await h.Events.PublishAsync(MessageKinds.InvoiceCreditNote, new NotificationEventContext(
            "Invoice", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["invoiceNumber"] = "CN-1", ["customerName"] = "Haven BV" })
        { CustomerId = h.CustomerId }, CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("credit@haven.be", message.RecipientAddress);
    }
}
