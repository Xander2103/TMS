using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

public class InternalMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid SenderId, Guid RecipientId, Guid RoleId)
    {
        public InternalMessageService For(Guid userId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var clock = new TestClock(Now);
            return new InternalMessageService(Db.Context, tenant, user,
                new NotificationService(Db.Context, tenant, user, clock),
                new AuditService(Db.Context, tenant, user), clock);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.AddRange(
            new User { Id = senderId, TenantId = tenantId, Email = "hr@acme.be", FirstName = "Hilde", LastName = "HR", IsActive = true },
            new User { Id = recipientId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Chauffeur", IsActive = true });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Chauffeur", IsActive = true });
        db.Context.UserRoles.Add(new UserRole { UserId = recipientId, RoleId = roleId });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, senderId, recipientId, roleId);
    }

    [Fact]
    public async Task Send_ToUserAndRole_DedupesRecipients_AndNotifiesThem()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var count = await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Nieuwe planning", "De planning van volgende week staat klaar.",
            UserIds: [h.RecipientId], RoleId: h.RoleId), CancellationToken.None);

        Assert.Equal(1, count); // user + role member are the same person
        var inbox = await h.For(h.RecipientId).ListInboxAsync(CancellationToken.None);
        var message = Assert.Single(inbox);
        Assert.Equal("Nieuwe planning", message.Subject);
        Assert.Equal("Hilde HR", message.SenderName);
        Assert.Null(message.ReadAt);

        Assert.Equal(1, await h.For(h.RecipientId).UnreadCountAsync(CancellationToken.None));
        Assert.Contains(h.Db.Context.Notifications, n => n.UserId == h.RecipientId && n.Type == "internal_message");

        // The sender's own inbox stays empty; the sent list shows the fan-out size.
        Assert.Empty(await h.For(h.SenderId).ListInboxAsync(CancellationToken.None));
        var sent = Assert.Single(await h.For(h.SenderId).ListSentAsync(CancellationToken.None));
        Assert.Equal(1, sent.RecipientCount);
    }

    [Fact]
    public async Task MarkRead_ClearsUnread_OnlyForTheRecipient()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.SenderId).SendAsync(new SendInternalMessageRequest(
            "Onderwerp", "Inhoud", UserIds: [h.RecipientId]), CancellationToken.None);
        var messageId = (await h.For(h.RecipientId).ListInboxAsync(CancellationToken.None)).Single().Id;

        // Someone who is not a recipient cannot mark it.
        Assert.False(await h.For(h.SenderId).MarkReadAsync(messageId, CancellationToken.None));

        Assert.True(await h.For(h.RecipientId).MarkReadAsync(messageId, CancellationToken.None));
        Assert.Equal(0, await h.For(h.RecipientId).UnreadCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Send_RequiresSubjectBodyAndAtLeastOneRecipient()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.SenderId);

        var noSubject = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.SendAsync(new SendInternalMessageRequest(" ", "x", UserIds: [h.RecipientId]), CancellationToken.None));
        Assert.Contains("subject", noSubject.FieldErrors!.Keys);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.SendAsync(new SendInternalMessageRequest("Onderwerp", "x"), CancellationToken.None));

        // Unknown recipients are refused (tenant safety).
        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.SendAsync(new SendInternalMessageRequest("Onderwerp", "x", UserIds: [Guid.NewGuid()]), CancellationToken.None));
    }
}
