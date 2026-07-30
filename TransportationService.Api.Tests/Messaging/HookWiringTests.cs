using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// Fix round 1 (phase 6 review): end-to-end coverage THROUGH the real service methods for the
/// hook call sites that previously had no test exercising the actual glue code — a wrong event
/// key, a CustomerId mix-up, or an accidentally deleted PublishEventAsync call would have gone
/// undetected. Mirrors OrderNotificationEventTests' style: seed a recipient the catalog default
/// resolves to, call the real service method, assert the outbox row / in-app notification exists.
/// </summary>
public class HookWiringTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private static NotificationEventService BuildEvents(SqliteTestDbContext db, DevTenantContext tenant, TestClock clock)
    {
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var outbox = new MessageOutboxService(db.Context, tenant, clock);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var communication = new CustomerCommunicationService(db.Context, tenant, audit);
        return new NotificationEventService(db.Context, tenant, outbox, notifications, communication, NullLogger<NotificationEventService>.Instance);
    }

    private static async Task<(Guid UserId, Guid RoleId)> SeedPermissionHolderAsync(
        SqliteTestDbContext db, Guid tenantId, string permissionCode, string email = "staff@acme.be")
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = email, PasswordHash = "x", FirstName = "Staf", LastName = "Fer",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Holder", IsActive = true });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = permissionCode, Module = "test", Action = "test" });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await db.Context.SaveChangesAsync();
        return (userId, roleId);
    }

    // --- InvoiceService.CreateAsync -> invoice_draft_ready ---

    [Fact]
    public async Task InvoiceCreate_Publishes_InvoiceDraftReady_InApp_ToInvoicesViewHolders()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", VatNumber = "BE0123456789", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 1450m,
        });
        var (holderId, _) = await SeedPermissionHolderAsync(db, tenantId, "invoices.view");

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var events = BuildEvents(db, tenant, clock);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var sut = new InvoiceService(db.Context, tenant, audit, clock, new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, audit),
            events);

        var result = await sut.CreateAsync(new CreateInvoiceRequest(customerId, null, [orderId], [], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);

        var mine = new NotificationService(db.Context, tenant, new DevCurrentUserContext(holderId), clock);
        var notified = await mine.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        var notice = Assert.Single(notified, n => n.Type == MessageKinds.InvoiceDraftReady);
        Assert.Contains(result.Invoice!.InvoiceNumber, notice.Message);
    }

    // --- InvoiceService.ChangeStatusAsync(Sent) -> invoice_sent ---

    [Fact]
    public async Task InvoiceChangeStatusToSent_Publishes_InvoiceSent_EmailToCommunicationRuleContact()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", VatNumber = "BE0123456789", IsActive = true });
        db.Context.CustomerContacts.Add(new CustomerContact
        {
            Id = contactId, TenantId = tenantId, CustomerId = customerId, FirstName = "Boek", LastName = "Houding",
            Email = "facturatie@haven.be", IsActive = true,
        });
        db.Context.CustomerCommunicationRules.Add(new CustomerCommunicationRule
        {
            Id = ruleId, TenantId = tenantId, CustomerId = customerId, Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true,
        });
        db.Context.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RuleId = ruleId, ContactId = contactId,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0002",
            OrderDate = new(2026, 7, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "10 paletten", AgreedPrice = 900m,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var events = BuildEvents(db, tenant, clock);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var sut = new InvoiceService(db.Context, tenant, audit, clock, new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, audit),
            events);

        var created = await sut.CreateAsync(new CreateInvoiceRequest(customerId, null, [orderId], [], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var invoiceId = created.Invoice!.Id;
        var invoiceNumber = created.Invoice.InvoiceNumber;

        var sent = await sut.ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);

        var message = Assert.Single(db.Context.OutboxMessages, m => m.Kind == MessageKinds.InvoiceSent);
        Assert.Equal("facturatie@haven.be", message.RecipientAddress);
        Assert.Equal($"{MessageKinds.InvoiceSent}:Invoice:{invoiceId}:facturatie@haven.be", message.IdempotencyKey);
        Assert.Contains(invoiceNumber!, message.Body);
    }

    // --- DamageReportService.CreateAsync -> fleet_damage_created ---

    [Fact]
    public async Task DamageReportCreate_Publishes_FleetDamageCreated_InApp_ToMaintenancePoliciesViewHolders()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-ABC-123", IsActive = true });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        var (holderId, _) = await SeedPermissionHolderAsync(db, tenantId, "maintenance_policies.view");

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var events = BuildEvents(db, tenant, clock);
        var sut = new DamageReportService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), events);

        var result = await sut.CreateForVehicleAsync(vehicleId,
            new CreateDamageReportRequest(driverId, new DateOnly(2026, 7, 30), "E313 Antwerpen-Oost", "Spiegel afgereden bij laden",
                DamageSeverity.Minor, "VERZ-2026-002", null),
            CancellationToken.None);
        Assert.Equal(DamageOperationOutcome.Success, result.Outcome);

        var mine = new NotificationService(db.Context, tenant, new DevCurrentUserContext(holderId), clock);
        var notified = await mine.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        var notice = Assert.Single(notified, n => n.Type == MessageKinds.FleetDamageCreated);
        Assert.Contains("1-ABC-123", notice.Message);
    }

    // --- AbsenceService.CreateForEmployeeAsync -> leave_requested ---

    [Fact]
    public async Task AbsenceCreate_Publishes_LeaveRequested_InApp_ToAbsencesApproveHolders()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        var (holderId, _) = await SeedPermissionHolderAsync(db, tenantId, "absences.approve");

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var events = BuildEvents(db, tenant, clock);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, clock);
        var sut = new AbsenceService(db.Context, tenant, currentUser, audit, notifications,
            new LocalFileStorageService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ts-hook-wiring-tests", Guid.NewGuid().ToString("N"))),
            new NoOpCalendarSyncService(), clock, events);

        var result = await sut.CreateForEmployeeAsync(employeeId,
            new CreateAbsenceRequest(AbsenceType.Vacation, new(2026, 8, 3), new(2026, 8, 14), null),
            CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, result.Outcome);

        var mine = new NotificationService(db.Context, tenant, new DevCurrentUserContext(holderId), clock);
        var notified = await mine.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None);
        Assert.Contains(notified, n => n.Type == MessageKinds.LeaveRequested);
    }
}
