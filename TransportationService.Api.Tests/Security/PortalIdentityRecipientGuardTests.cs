using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Fix wave B, item B2 (pass-2 finding I-3). Three "notify everyone holding permission X" queries
/// fan internal traffic out by role→permission alone. A customer-linked account that still carries
/// a legacy internal grant (see <c>DefaultRoleUpgrades</c>, which never removes) therefore stayed a
/// valid RECIPIENT of internal in-app notifications and internal staff e-mail, even after the H-14
/// identity-class guard stopped it from CALLING anything internal.
///
/// The rule is the same one the evaluators apply: only an INTERNAL identity (no customer link)
/// receives internal traffic. Each test seeds one role holding <c>orders.manage</c> and grants it
/// to two accounts that differ only by <c>User.CustomerId</c>.
/// </summary>
public class PortalIdentityRecipientGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid PortalUserId, Guid InternalUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, DefaultLanguage = "nl" });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        db.Context.Users.AddRange(
            new User
            {
                Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", PasswordHash = "x",
                FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = internalUserId, TenantId = tenantId, Email = "planner@acme.be", PasswordHash = "x",
                FirstName = "Peter", LastName = "Planner", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Roles.Add(new Role
        {
            Id = roleId, TenantId = tenantId, Name = "Klantportaal (legacy)", TemplateCode = "klantportaal", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = PermissionCodes.OrdersManage, Module = "orders", Action = "manage", Description = "x",
        });
        db.Context.UserRoles.AddRange(
            new UserRole { UserId = portalUserId, RoleId = roleId },
            new UserRole { UserId = internalUserId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantId, portalUserId, internalUserId);
    }

    private sealed class ThrowingProvider : IMessageChannelProvider
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
            => throw new InvalidOperationException("smtp down");
    }

    [Fact]
    public async Task InAppPermissionFanOut_SkipsCustomerLinkedAccounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new NotificationService(h.Db.Context, tenant, new DevCurrentUserContext(h.InternalUserId), new TestClock(Now));

        await sut.NotifyPermissionHoldersAsync(
            PermissionCodes.OrdersManage, "test_event", "Intern", "Interne melding", "/orders", CancellationToken.None);

        var recipients = await h.Db.Context.Notifications.AsNoTracking().Select(n => n.UserId).ToListAsync();
        Assert.Equal([h.InternalUserId], recipients);
    }

    [Fact]
    public async Task InternalStaffEmailFanOut_SkipsCustomerLinkedAccounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(h.TenantId);
        var currentUser = new DevCurrentUserContext(null);
        var notifications = new NotificationService(h.Db.Context, tenant, currentUser, clock);
        var outbox = new MessageOutboxService(h.Db.Context, tenant, clock);
        var communication = new CustomerCommunicationService(
            h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, currentUser));
        var sut = new NotificationEventService(
            h.Db.Context, tenant, outbox, notifications, communication, NullLogger<NotificationEventService>.Instance);

        // A tenant-authored rule that fans an event out to holders of an INTERNAL code — the
        // configuration pass 2 named as the one that makes this reachable in production.
        h.Db.Context.NotificationRules.Add(new NotificationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EventKey = MessageKinds.OrderCreated,
            Enabled = true, InAppEnabled = false, EmailEnabled = true,
            RecipientsJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.OrdersManage),
            }),
        });
        await h.Db.Context.SaveChangesAsync();

        await sut.PublishAsync(MessageKinds.OrderCreated, new NotificationEventContext(
                "TransportOrder", Guid.NewGuid().ToString(),
                new Dictionary<string, string> { ["orderNumber"] = "ORD-1", ["customerName"] = "Haven BV", ["goodsDescription"] = "" }),
            CancellationToken.None);

        var addresses = await h.Db.Context.OutboxMessages.AsNoTracking().Select(m => m.RecipientAddress).ToListAsync();
        Assert.Equal(["planner@acme.be"], addresses);
    }

    [Fact]
    public async Task PermanentDeliveryFailureAlert_SkipsCustomerLinkedAccounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            Channel = MessageChannel.Email,
            Kind = MessageKinds.OrderCreated,
            OwnerType = MessageOwnerType.Customer,
            OwnerId = Guid.NewGuid(),
            RecipientAddress = "klant@haven.be",
            Subject = "Opdracht",
            Body = "tekst",
            Status = OutboxStatus.Pending,
            // One attempt away from the permanent-failure threshold, so this run alerts staff.
            AttemptCount = 4,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var dispatcher = new MessageDispatcher(
            h.Db.Context, new ThrowingProvider(), new ThrowingProvider(), new TestClock(Now));
        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        var alerted = await h.Db.Context.Notifications.AsNoTracking()
            .Where(n => n.Type == "customer_notification_failed")
            .Select(n => n.UserId)
            .ToListAsync();
        Assert.Equal([h.InternalUserId], alerted);
    }
}
