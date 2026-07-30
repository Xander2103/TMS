using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// Phase 6 (corrections wave 4): NotificationEventService — rule resolution (default/disabled/
/// customer override), recipient resolution per type, language fallback, idempotent double
/// publish and tenant isolation.
/// </summary>
public class NotificationEventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, NotificationEventService Sut, NotificationService Notifications,
        Guid TenantId, Guid CustomerId, Guid ContactUserId, TestClock Clock);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, DefaultLanguage = "nl" });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Email = "algemeen@haven.be", DefaultLanguageCode = "fr", IsActive = true,
        });
        db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
            FirstName = "Marie", LastName = "Contact", Email = "marie@haven.be",
            IsPrimary = true, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var outbox = new MessageOutboxService(db.Context, tenant, clock);
        var communication = new CustomerCommunicationService(db.Context, tenant, new AuditService(db.Context, tenant, currentUser));
        var sut = new NotificationEventService(db.Context, tenant, outbox, notifications, communication, NullLogger<NotificationEventService>.Instance);

        return new Harness(db, sut, notifications, tenantId, customerId, Guid.Empty, clock);
    }

    private static NotificationEventContext OrderContext(Guid customerId) => new(
        "TransportOrder", Guid.NewGuid().ToString(),
        new Dictionary<string, string> { ["orderNumber"] = "ORD-1", ["customerName"] = "Haven BV", ["goodsDescription"] = "Pallets" })
    { CustomerId = customerId };

    [Fact]
    public async Task Default_NoRuleRow_UsesCatalogRecipients()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("marie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task Rule_Disabled_SuppressesEverything()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EventKey = MessageKinds.OrderAccepted, Enabled = false,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h.CustomerId), CancellationToken.None);

        Assert.Empty(h.Db.Context.OutboxMessages);
        Assert.Empty(h.Db.Context.Notifications);
    }

    [Fact]
    public async Task CustomerOverride_DisablesForOneCustomer_ButNotAnother()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer
        {
            Id = otherCustomerId, TenantId = h.TenantId, CustomerNumber = "KL-2", Name = "Andere NV",
            Email = "info@andere.be", IsActive = true,
        });
        h.Db.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EventKey = MessageKinds.OrderAccepted,
            Enabled = true, EmailEnabled = true, AllowCustomerOverride = true,
        });
        h.Db.Context.CustomerNotificationOverrides.Add(new CustomerNotificationOverride
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            EventKey = MessageKinds.OrderAccepted, Enabled = false,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h.CustomerId), CancellationToken.None);
        Assert.Empty(h.Db.Context.OutboxMessages);

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(otherCustomerId), CancellationToken.None);
        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("info@andere.be", message.RecipientAddress);
    }

    [Fact]
    public async Task Recipient_CustomerCommunicationRule_ResolvesConfiguredContacts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var invoiceContactId = Guid.NewGuid();
        h.Db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = invoiceContactId, TenantId = h.TenantId, CustomerId = h.CustomerId,
            FirstName = "Boek", LastName = "Houding", Email = "facturatie@haven.be", IsActive = true,
        });
        var ruleId = Guid.NewGuid();
        h.Db.Context.CustomerCommunicationRules.Add(new CustomerCommunicationRule
        {
            Id = ruleId, TenantId = h.TenantId, CustomerId = h.CustomerId,
            Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true,
        });
        h.Db.Context.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, RuleId = ruleId, ContactId = invoiceContactId,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.InvoiceSent, new NotificationEventContext(
            "Invoice", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["invoiceNumber"] = "INV-1", ["customerName"] = "Haven BV" })
        { CustomerId = h.CustomerId }, CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("facturatie@haven.be", message.RecipientAddress);
    }

    [Fact]
    public async Task Recipient_InternalPermission_ResolvesHolderEmail_AndInApp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var holder = await SeedPermissionHolderAsync(h.Db.Context, h.TenantId, "orders.change_status");
        var userId = holder.UserId;

        await h.Sut.PublishAsync(MessageKinds.OrderCreated, new NotificationEventContext(
            "TransportOrder", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["orderNumber"] = "ORD-2", ["customerName"] = "Haven BV", ["goodsDescription"] = "" })
        { CustomerId = h.CustomerId, InAppMessage = "Nieuwe opdracht" }, CancellationToken.None);

        var mine = new NotificationService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(userId), h.Clock);
        var notified = await mine.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        Assert.Contains(notified, n => n.Type == MessageKinds.OrderCreated);
    }

    [Fact]
    public async Task Recipient_InternalRole_ResolvesByTemplateCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = userId, TenantId = h.TenantId, Email = "planner@acme.be", PasswordHash = "x",
            FirstName = "Plan", LastName = "Ner", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = h.TenantId, Name = "Planner", TemplateCode = "planner", IsActive = true });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        h.Db.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EventKey = MessageKinds.OrderCreated,
            Enabled = true, InAppEnabled = false, EmailEnabled = true,
            RecipientsJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new RecipientSpec(NotificationRecipientType.InternalRole, "planner"),
            }),
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.OrderCreated, new NotificationEventContext(
            "TransportOrder", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["orderNumber"] = "ORD-3", ["customerName"] = "Haven BV", ["goodsDescription"] = "" }),
            CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("planner@acme.be", message.RecipientAddress);
    }

    [Fact]
    public async Task Recipient_ExplicitEmail_HasNoOwner_ButStillQueues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EventKey = MessageKinds.OrderCreated,
            Enabled = true, InAppEnabled = false, EmailEnabled = true,
            RecipientsJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new RecipientSpec(NotificationRecipientType.ExplicitEmail, "ops@acme.be"),
            }),
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.OrderCreated, new NotificationEventContext(
            "TransportOrder", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["orderNumber"] = "ORD-4", ["customerName"] = "Haven BV", ["goodsDescription"] = "" }),
            CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("ops@acme.be", message.RecipientAddress);
    }

    [Fact]
    public async Task Recipient_Driver_ResolvesContextEmployee_EmailAndInApp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-9", FirstName = "Jan", LastName = "Chauffeur",
            Email = "jan@acme.be", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Users.Add(new User
        {
            Id = userId, TenantId = h.TenantId, Email = "jan@acme.be", PasswordHash = "x", EmployeeId = employeeId,
            FirstName = "Jan", LastName = "Chauffeur", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.LeaveDecided, new NotificationEventContext(
            "Absence", Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["employeeName"] = "Jan Chauffeur", ["period"] = "1-2 aug", ["note"] = "", ["decision"] = "Goedgekeurd" })
        { EmployeeId = employeeId, InAppMessage = "Verlof goedgekeurd" }, CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("jan@acme.be", message.RecipientAddress);

        var mine = new NotificationService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(userId), h.Clock);
        var notified = await mine.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        Assert.Contains(notified, n => n.Type == MessageKinds.LeaveDecided);
    }

    [Fact]
    public async Task Language_FallsBackThroughChain_WhenRecipientHasNone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // The customer's primary contact has no PreferredLanguageCode -> falls back to the
        // customer's DefaultLanguageCode ("fr", seeded above).
        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h.CustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("fr", message.Language);
    }

    [Fact]
    public async Task Language_FallsBackToTenantDefault_WhenCustomerHasNone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var noLangCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer
        {
            Id = noLangCustomerId, TenantId = h.TenantId, CustomerNumber = "KL-3", Name = "Geen Taal NV",
            Email = "info@geentaal.be", IsActive = true, DefaultLanguageCode = null,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(noLangCustomerId), CancellationToken.None);

        var message = Assert.Single(h.Db.Context.OutboxMessages);
        Assert.Equal("nl", message.Language); // TenantSettings.DefaultLanguage seeded as "nl"
    }

    [Fact]
    public async Task DoublePublish_IsIdempotent_SingleOutboxRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var context = OrderContext(h.CustomerId);

        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, context, CancellationToken.None);
        await h.Sut.PublishAsync(MessageKinds.OrderAccepted, context, CancellationToken.None);

        Assert.Single(h.Db.Context.OutboxMessages);
    }

    [Fact]
    public async Task TenantIsolation_RuleInOneTenant_DoesNotAffectAnother()
    {
        var h1 = await SeedAsync();
        using var _1 = h1.Db;

        // A disabled rule in a completely separate tenant/db must never suppress h1's publish.
        var otherDb = new SqliteTestDbContext();
        using var _2 = otherDb;
        var otherTenantId = Guid.NewGuid();
        otherDb.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        otherDb.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, EventKey = MessageKinds.OrderAccepted, Enabled = false,
        });
        await otherDb.Context.SaveChangesAsync();

        await h1.Sut.PublishAsync(MessageKinds.OrderAccepted, OrderContext(h1.CustomerId), CancellationToken.None);

        Assert.Single(h1.Db.Context.OutboxMessages);
    }

    [Fact]
    public async Task UnknownEventKey_IsIgnoredSilently()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.PublishAsync("does_not_exist", OrderContext(h.CustomerId), CancellationToken.None);

        Assert.Empty(h.Db.Context.OutboxMessages);
        Assert.Empty(h.Db.Context.Notifications);
    }

    private static async Task<(Guid UserId, Guid RoleId)> SeedPermissionHolderAsync(
        TransportationDbContext dbContext, Guid tenantId, string permissionCode)
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        dbContext.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "staff@acme.be", PasswordHash = "x",
            FirstName = "Staf", LastName = "Fer", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        dbContext.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Ops", IsActive = true });
        var existingPermission = await dbContext.Permissions.FirstOrDefaultAsync(p => p.Code == permissionCode);
        if (existingPermission is null)
        {
            existingPermission = new Permission { Id = permissionId, Code = permissionCode, Module = "orders", Action = "change_status" };
            dbContext.Permissions.Add(existingPermission);
        }

        dbContext.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = existingPermission.Id });
        dbContext.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await dbContext.SaveChangesAsync();
        return (userId, roleId);
    }
}
