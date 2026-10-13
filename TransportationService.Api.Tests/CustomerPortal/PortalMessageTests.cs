using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// Sprint fase 5: meertalige portaalberichten — targeting, taalresolutie met fallback,
/// klantisolatie, read/acknowledge-receipts, e-mail in voorkeurstaal, bulkgate en intrekken.
/// </summary>
public class PortalMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 01, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid StaffId, Guid CustomerAId, Guid CustomerBId,
        Guid UserA1, Guid UserA2, Guid UserB1, TestClock Clock)
    {
        public PortalMessageService For(Guid userId, bool holdsBulk = true, bool withOutbox = false)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var permissions = holdsBulk
                ? (IPermissionAuthorizationService)new InventoryTestFactory.AllowAllPermissionService()
                : new InventoryTestFactory.DenyAllPermissionService();
            return new PortalMessageService(Db.Context, tenant, user,
                new AuditService(Db.Context, tenant, user), Clock, permissions,
                withOutbox ? new MessageOutboxService(Db.Context, tenant, Clock) : null);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        var userB1 = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerAId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true, DefaultLanguageCode = "fr" },
            new Customer { Id = customerBId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = staffId, TenantId = tenantId, Email = "office@acme.be", FirstName = "Olga", LastName = "Office", IsActive = true },
            new User { Id = userA1, TenantId = tenantId, Email = "a1@haven.be", FirstName = "Anna", LastName = "Un", CustomerId = customerAId, IsActive = true, PreferredLanguageCode = "en" },
            new User { Id = userA2, TenantId = tenantId, Email = "a2@haven.be", FirstName = "Bert", LastName = "Deux", CustomerId = customerAId, IsActive = true },
            new User { Id = userB1, TenantId = tenantId, Email = "b1@andere.be", FirstName = "Chris", LastName = "Ander", CustomerId = customerBId, IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, staffId, customerAId, customerBId, userA1, userA2, userB1, new TestClock(Now));
    }

    private static SendPortalMessageRequest Message(
        IReadOnlyList<Guid>? customerIds = null, IReadOnlyList<Guid>? portalUserIds = null,
        bool ack = false, bool email = false, PortalMessageDisplayMode mode = PortalMessageDisplayMode.Notification) =>
        new("Onderhoud gepland", "Zaterdag onderhoud.",
            TitleFr: "Maintenance prévue", BodyFr: "Maintenance samedi.",
            TitleEn: null, BodyEn: null,
            CustomerIds: customerIds, PortalUserIds: portalUserIds,
            RequiresAcknowledgement: ack, SendEmail: email, DisplayMode: mode);

    [Fact]
    public async Task Feed_IsCustomerScoped_AndResolvesLanguageWithFallback()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId]), CancellationToken.None);

        // Anna prefers EN; EN content missing → falls back to NL.
        var annaFeed = await h.For(h.UserA1).ListFeedAsync(CancellationToken.None);
        var annaItem = Assert.Single(annaFeed!);
        Assert.Equal("en", annaItem.Language);
        Assert.Equal("Onderhoud gepland", annaItem.Title);

        // Bert has no preference; customer default is FR → French content.
        var bertFeed = await h.For(h.UserA2).ListFeedAsync(CancellationToken.None);
        Assert.Equal("Maintenance prévue", Assert.Single(bertFeed!).Title);

        // Customer B sees nothing, and the message id is a 404 for them.
        Assert.Empty((await h.For(h.UserB1).ListFeedAsync(CancellationToken.None))!);
        var messageId = (await h.Db.Context.PortalMessages.SingleAsync()).Id;
        Assert.False(await h.For(h.UserB1).MarkFeedReadAsync(messageId, CancellationToken.None));
    }

    [Fact]
    public async Task SingleUserTargeting_ReachesOnlyThatUser()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId).SendAsync(Message(portalUserIds: [h.UserA1]), CancellationToken.None);

        Assert.Single((await h.For(h.UserA1).ListFeedAsync(CancellationToken.None))!);
        Assert.Empty((await h.For(h.UserA2).ListFeedAsync(CancellationToken.None))!);
    }

    [Fact]
    public async Task ReadAndAcknowledge_StampReceipts_AndDriveUnreadCount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId], ack: true), CancellationToken.None);
        var messageId = (await h.Db.Context.PortalMessages.SingleAsync()).Id;

        Assert.Equal(1, await h.For(h.UserA1).FeedUnreadCountAsync(CancellationToken.None));
        Assert.True(await h.For(h.UserA1).MarkFeedReadAsync(messageId, CancellationToken.None));
        Assert.Equal(0, await h.For(h.UserA1).FeedUnreadCountAsync(CancellationToken.None));
        Assert.True(await h.For(h.UserA1).AcknowledgeFeedAsync(messageId, CancellationToken.None));

        var status = await h.For(h.StaffId).GetDeliveryStatusAsync(messageId, CancellationToken.None);
        var anna = status!.Recipients.Single(r => r.UserId == h.UserA1);
        Assert.NotNull(anna.ReadAt);
        Assert.NotNull(anna.AcknowledgedAt);
        var bert = status.Recipients.Single(r => r.UserId == h.UserA2);
        Assert.Null(bert.ReadAt);

        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(), l => l.Action == "AcknowledgedInPortal");
    }

    [Fact]
    public async Task AcknowledgeOnNonAckMessage_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId]), CancellationToken.None);
        var messageId = (await h.Db.Context.PortalMessages.SingleAsync()).Id;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.UserA1).AcknowledgeFeedAsync(messageId, CancellationToken.None));
    }

    [Fact]
    public async Task MultiCustomer_RequiresBulkPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.StaffId, holdsBulk: false).SendAsync(
                Message(customerIds: [h.CustomerAId, h.CustomerBId]), CancellationToken.None));

        // Single customer works without the bulk permission.
        var sent = await h.For(h.StaffId, holdsBulk: false).SendAsync(
            Message(customerIds: [h.CustomerAId]), CancellationToken.None);
        Assert.Single(sent.CustomerNames);
    }

    [Fact]
    public async Task RelatedEntity_RequiresSingleCustomerAndOwnership()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Two customers + related entity → refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId, h.CustomerBId]) with
            {
                RelatedEntityType = "order",
                RelatedEntityId = Guid.NewGuid(),
            }, CancellationToken.None));

        // Unknown/foreign order id → refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId]) with
            {
                RelatedEntityType = "order",
                RelatedEntityId = Guid.NewGuid(),
            }, CancellationToken.None));
    }

    [Fact]
    public async Task SendEmail_UsesRecipientLanguage_PerPortalUser()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId, withOutbox: true).SendAsync(
            Message(customerIds: [h.CustomerAId], email: true), CancellationToken.None);

        var outbox = await h.Db.Context.OutboxMessages.OrderBy(m => m.RecipientAddress).ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.All(outbox, m => Assert.Equal(MessageKinds.PortalMessagePublished, m.Kind));

        // Anna (EN pref, EN content missing) gets the NL fallback content in an "en" mail;
        // Bert follows the customer default FR and receives the French body.
        var anna = outbox.Single(m => m.RecipientAddress == "a1@haven.be");
        Assert.Equal("en", anna.Language);
        Assert.Contains("Zaterdag onderhoud.", anna.Body);
        var bert = outbox.Single(m => m.RecipientAddress == "a2@haven.be");
        Assert.Equal("fr", bert.Language);
        Assert.Contains("Maintenance samedi.", bert.Body);
    }

    [Fact]
    public async Task Cancel_HidesFromFeed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.StaffId).SendAsync(Message(customerIds: [h.CustomerAId]), CancellationToken.None);
        var messageId = (await h.Db.Context.PortalMessages.SingleAsync()).Id;

        Assert.True(await h.For(h.StaffId).CancelAsync(messageId, CancellationToken.None));
        Assert.Empty((await h.For(h.UserA1).ListFeedAsync(CancellationToken.None))!);
        Assert.Equal(0, await h.For(h.UserA1).FeedUnreadCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BlockingDisplayMode_ForcesAcknowledgementFlag()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sent = await h.For(h.StaffId).SendAsync(
            Message(customerIds: [h.CustomerAId], mode: PortalMessageDisplayMode.BlockingAcknowledgement),
            CancellationToken.None);
        Assert.True(sent.RequiresAcknowledgement);
    }
}
