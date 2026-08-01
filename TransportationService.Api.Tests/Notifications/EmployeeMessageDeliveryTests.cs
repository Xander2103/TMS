using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

/// <summary>
/// Sprint fase 4: bulk targeting (rol/afdeling/iedereen) achter messages.send_bulk,
/// bevestiging vs. gelezen, zichtbaarheidsvenster, intrekken, bezorgstatus en e-mailkanaal
/// via de outbox (idempotent per ontvanger).
/// </summary>
public class EmployeeMessageDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 01, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid SenderId, Guid WorkerUserId, Guid WorkerEmployeeId,
        Guid OtherUserId, Guid DepartmentId, TestClock Clock)
    {
        public InternalMessageService For(Guid userId, bool holdsBulk = true, bool withOutbox = false)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var permissions = holdsBulk
                ? (IPermissionAuthorizationService)new InventoryTestFactory.AllowAllPermissionService()
                : new InventoryTestFactory.DenyAllPermissionService();
            return new InternalMessageService(Db.Context, tenant, user,
                new NotificationService(Db.Context, tenant, user, Clock),
                new AuditService(Db.Context, tenant, user), Clock, permissions,
                withOutbox ? new MessageOutboxService(Db.Context, tenant, Clock) : null);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var workerUserId = Guid.NewGuid();
        var workerEmployeeId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "MAG", Name = "Magazijn", IsActive = true });
        db.Context.Employees.Add(new Employee
        {
            Id = workerEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            Email = "jan@acme.be", DepartmentId = departmentId, IsActive = true,
        });
        db.Context.Users.AddRange(
            new User { Id = senderId, TenantId = tenantId, Email = "hr@acme.be", FirstName = "Hilde", LastName = "HR", IsActive = true },
            new User { Id = workerUserId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Janssen", EmployeeId = workerEmployeeId, IsActive = true },
            new User { Id = otherUserId, TenantId = tenantId, Email = "piet@acme.be", FirstName = "Piet", LastName = "Peeters", IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, senderId, workerUserId, workerEmployeeId, otherUserId, departmentId, new TestClock(Now));
    }

    [Fact]
    public async Task DepartmentTargeting_ExpandsToLinkedUsers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var count = await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Inventaris", "Vrijdag telling.", DepartmentId: h.DepartmentId), CancellationToken.None);

        Assert.Equal(1, count); // only Jan is linked to the department
        var inbox = await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None);
        Assert.Single(inbox);
        Assert.Empty(await h.For(h.OtherUserId).ListInboxAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BulkTargeting_WithoutBulkPermission_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.SenderId, holdsBulk: false);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.SendAsync(new SendInternalMessageRequest("X", "Y", DepartmentId: h.DepartmentId), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.SendAsync(new SendInternalMessageRequest("X", "Y", AllEmployees: true), CancellationToken.None));

        // Single explicit recipient stays allowed without the bulk permission.
        var count = await sut.SendAsync(new SendInternalMessageRequest("X", "Y", UserIds: [h.WorkerUserId]), CancellationToken.None);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Acknowledge_IsSeparateFromRead_AndOnlyForAckMessages()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Reglement", "Bevestig het nieuwe reglement.", UserIds: [h.WorkerUserId],
            RequiresAcknowledgement: true), CancellationToken.None);
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Info", "Gewoon ter info.", UserIds: [h.WorkerUserId]), CancellationToken.None);

        var inbox = await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None);
        var ackMessage = inbox.Single(m => m.RequiresAcknowledgement);
        var infoMessage = inbox.Single(m => !m.RequiresAcknowledgement);

        Assert.True(await h.For(h.WorkerUserId).AcknowledgeAsync(ackMessage.Id, CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.WorkerUserId).AcknowledgeAsync(infoMessage.Id, CancellationToken.None));

        var after = await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None);
        var acknowledged = after.Single(m => m.Id == ackMessage.Id);
        Assert.NotNull(acknowledged.AcknowledgedAt);
        Assert.NotNull(acknowledged.ReadAt); // acknowledging implies reading
    }

    [Fact]
    public async Task VisibleFrom_HidesUntilDue_AndSkipsImmediateNotification()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Later", "Pas morgen zichtbaar.", UserIds: [h.WorkerUserId],
            VisibleFrom: Now.UtcDateTime.AddDays(1)), CancellationToken.None);

        Assert.Empty(await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None));
        Assert.Equal(0, await h.For(h.WorkerUserId).UnreadCountAsync(CancellationToken.None));
        Assert.Empty(await h.Db.Context.Notifications.Where(n => n.Type == "internal_message").ToListAsync());
        Assert.Null((await h.Db.Context.Set<InternalMessage>().SingleAsync()).NotifiedAt);

        h.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        Assert.Single(await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None));
        Assert.Equal(1, await h.For(h.WorkerUserId).UnreadCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_HidesFromInbox_OnlySenderOrPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Oeps", "Verkeerd bericht.", UserIds: [h.WorkerUserId]), CancellationToken.None);
        var messageId = (await h.Db.Context.Set<InternalMessage>().SingleAsync()).Id;

        // A stranger without messages.cancel cannot withdraw it (404-equivalent).
        Assert.False(await h.For(h.OtherUserId, holdsBulk: false).CancelAsync(messageId, CancellationToken.None));

        Assert.True(await h.For(h.SenderId, holdsBulk: false).CancelAsync(messageId, CancellationToken.None));
        Assert.Empty(await h.For(h.WorkerUserId).ListInboxAsync(CancellationToken.None));
        Assert.Equal(0, await h.For(h.WorkerUserId).UnreadCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeliveryStatus_SenderSeesRecipients_StrangerWithoutPermissionDoesNot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Status", "Test.", UserIds: [h.WorkerUserId], RequiresAcknowledgement: true), CancellationToken.None);
        var messageId = (await h.Db.Context.Set<InternalMessage>().SingleAsync()).Id;
        await h.For(h.WorkerUserId).AcknowledgeAsync(messageId, CancellationToken.None);

        var status = await h.For(h.SenderId, holdsBulk: false).GetDeliveryStatusAsync(messageId, CancellationToken.None);
        Assert.NotNull(status);
        var row = Assert.Single(status!.Recipients);
        Assert.NotNull(row.ReadAt);
        Assert.NotNull(row.AcknowledgedAt);
        Assert.Equal("Geen", row.EmailStatus);

        Assert.Null(await h.For(h.OtherUserId, holdsBulk: false).GetDeliveryStatusAsync(messageId, CancellationToken.None));
    }

    [Fact]
    public async Task SendEmail_QueuesIdempotentOutboxCopies_AndLinksThemToRecipients()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.For(h.SenderId, withOutbox: true).SendAsync(new SendInternalMessageRequest(
            "Mail mee", "Belangrijk bericht.", UserIds: [h.WorkerUserId], SendEmail: true), CancellationToken.None);

        var outbox = Assert.Single(await h.Db.Context.OutboxMessages.ToListAsync());
        Assert.Equal(MessageKinds.EmployeeMessageReceived, outbox.Kind);
        Assert.Equal("jan@acme.be", outbox.RecipientAddress);
        var recipient = Assert.Single(await h.Db.Context.Set<InternalMessageRecipient>().ToListAsync());
        Assert.Equal(outbox.Id, recipient.EmailOutboxMessageId);

        var messageId = (await h.Db.Context.Set<InternalMessage>().SingleAsync()).Id;
        var status = await h.For(h.SenderId).GetDeliveryStatusAsync(messageId, CancellationToken.None);
        Assert.Equal("Pending", Assert.Single(status!.Recipients).EmailStatus);
    }
}
