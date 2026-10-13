using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 1 tail (C3, token-persistence hygiene): an outbox row that carried a one-time
/// activation link must not keep that link in durable storage once delivery is decided — the
/// dispatcher scrubs the body on success AND on permanent failure, while the row itself stays
/// as the audit trail of the send.
/// </summary>
public class Phase1TokenHygieneTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 31, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingProvider : IMessageChannelProvider
    {
        /// <summary>Body SNAPSHOT at send time — the dispatcher scrubs the entity afterwards.</summary>
        public List<string> SentBodies { get; } = [];

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            SentBodies.Add(message.Body);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProvider : IMessageChannelProvider
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
            => throw new InvalidOperationException("smtp down");
    }

    private static OutboxMessage InviteRow(Guid tenantId, int attemptCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Channel = MessageChannel.Email,
        Kind = MessageKinds.PortalUserInvited,
        OwnerType = MessageOwnerType.Customer,
        OwnerId = Guid.NewGuid(),
        RecipientAddress = "nieuw@haven.be",
        Subject = "Welkom",
        Body = "Activeer via https://portal.example/activeren?token=super-geheime-token&email=x",
        Status = OutboxStatus.Pending,
        AttemptCount = attemptCount,
        IdempotencyKey = $"portal_user_invited:User:{Guid.NewGuid()}:{Guid.NewGuid():N}"[..40],
        CreatedAt = Now.UtcDateTime,
        UpdatedAt = Now.UtcDateTime,
    };

    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        return (db, tenantId);
    }

    [Fact]
    public async Task SentInviteMail_NoLongerContainsTheActivationLink()
    {
        var (db, tenantId) = await SeedAsync();
        using var _ = db;
        db.Context.OutboxMessages.Add(InviteRow(tenantId));
        await db.Context.SaveChangesAsync();

        var email = new RecordingProvider();
        var dispatcher = new MessageDispatcher(db.Context, email, new RecordingProvider(), new TestClock(Now));
        Assert.Equal(1, await dispatcher.DispatchPendingAsync(10, CancellationToken.None));

        // The provider received the full body; the durable row did not keep it.
        Assert.Contains("super-geheime-token", Assert.Single(email.SentBodies));
        var row = await db.Context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.DoesNotContain("super-geheime-token", row.Body);
        Assert.Equal(MessageDispatcher.ScrubbedBodyMarker, row.Body);
    }

    [Fact]
    public async Task PermanentlyFailedInviteMail_IsScrubbedToo()
    {
        var (db, tenantId) = await SeedAsync();
        using var _ = db;
        // One attempt away from the permanent-failure threshold.
        db.Context.OutboxMessages.Add(InviteRow(tenantId, attemptCount: 4));
        await db.Context.SaveChangesAsync();

        var dispatcher = new MessageDispatcher(db.Context, new ThrowingProvider(), new ThrowingProvider(), new TestClock(Now));
        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        var row = await db.Context.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Kind == MessageKinds.PortalUserInvited && m.Status == OutboxStatus.Failed);
        Assert.DoesNotContain("super-geheime-token", row.Body);
    }

    [Fact]
    public async Task RetriedInviteMail_KeepsItsBodyUntilDeliveryIsDecided()
    {
        var (db, tenantId) = await SeedAsync();
        using var _ = db;
        db.Context.OutboxMessages.Add(InviteRow(tenantId));
        await db.Context.SaveChangesAsync();

        var dispatcher = new MessageDispatcher(db.Context, new ThrowingProvider(), new ThrowingProvider(), new TestClock(Now));
        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        // Still pending (backoff): a later attempt must still be able to deliver the real link.
        var row = await db.Context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Contains("super-geheime-token", row.Body);
    }

    [Fact]
    public async Task NonCredentialMail_IsNeverScrubbed()
    {
        var (db, tenantId) = await SeedAsync();
        using var _ = db;
        var row = InviteRow(tenantId);
        row.Kind = MessageKinds.PodAvailable;
        row.Body = "Uw POD voor ORD-0042 staat klaar.";
        db.Context.OutboxMessages.Add(row);
        await db.Context.SaveChangesAsync();

        var dispatcher = new MessageDispatcher(db.Context, new RecordingProvider(), new RecordingProvider(), new TestClock(Now));
        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        var stored = await db.Context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Sent, stored.Status);
        Assert.Equal("Uw POD voor ORD-0042 staat klaar.", stored.Body);
    }
}
