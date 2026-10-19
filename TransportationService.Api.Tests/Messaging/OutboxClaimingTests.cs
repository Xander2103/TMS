using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// H2 / poison-row hardening of the outbox dispatcher: atomic per-row claiming (two dispatchers
/// never send the same message), the outcome persisted after EACH message, interrupted sends held
/// for review instead of being resent, and one unreadable row quarantined while the batch goes on.
/// </summary>
public class OutboxClaimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private sealed class ScriptedProvider : IMessageChannelProvider
    {
        public List<Guid> Sent { get; } = [];

        /// <summary>Runs before the send is recorded; may throw to simulate a failure or shutdown.</summary>
        public Func<OutboxMessage, Task>? OnSend { get; set; }

        public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (OnSend is not null)
            {
                await OnSend(message);
            }

            Sent.Add(message.Id);
        }
    }

    private static OutboxMessage Row(Guid tenantId, int order, string kind = MessageKinds.PodAvailable) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Channel = MessageChannel.Email,
        Kind = kind,
        OwnerType = MessageOwnerType.Customer,
        OwnerId = Guid.NewGuid(),
        RecipientAddress = $"klant{order}@haven.be",
        Subject = $"Bericht {order}",
        Body = $"Inhoud {order}",
        Status = OutboxStatus.Pending,
        IdempotencyKey = $"test:{Guid.NewGuid():N}",
        CreatedAt = Now.UtcDateTime.AddMinutes(order),
        UpdatedAt = Now.UtcDateTime,
    };

    private static async Task<(SqliteTestDbContext Db, Guid TenantId, List<OutboxMessage> Rows)> SeedAsync(
        int count, params IInterceptor[] interceptors)
    {
        var db = new SqliteTestDbContext(null, interceptors);
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        var rows = Enumerable.Range(1, count).Select(i => Row(tenantId, i)).ToList();
        db.Context.OutboxMessages.AddRange(rows);
        await db.Context.SaveChangesAsync();
        return (db, tenantId, rows);
    }

    private static async Task<Dictionary<Guid, OutboxMessage>> ReadAllAsync(SqliteTestDbContext db, Guid tenantId)
    {
        await using var fresh = db.CreateContextForTenant(tenantId);
        return await fresh.OutboxMessages.AsNoTracking().ToDictionaryAsync(m => m.Id);
    }

    [Fact]
    public async Task TwoDispatchers_RunningInterleaved_SendEveryMessageExactlyOnce()
    {
        var (db, tenantId, rows) = await SeedAsync(4);
        using var _ = db;
        await using var secondContext = db.CreateContextForTenant(tenantId);
        var clock = new TestClock(Now);

        var providerB = new ScriptedProvider();
        var dispatcherB = new MessageDispatcher(secondContext, providerB, providerB, clock);

        // Dispatcher A selected its batch; while it is sending its FIRST message, a second
        // instance (B) runs a whole dispatch. B must skip A's claimed row and A must then lose
        // the claim on every row B took.
        var providerA = new ScriptedProvider();
        var reentered = false;
        providerA.OnSend = async _ =>
        {
            if (!reentered)
            {
                reentered = true;
                await dispatcherB.DispatchPendingAsync(10, CancellationToken.None);
            }
        };
        var dispatcherA = new MessageDispatcher(db.Context, providerA, providerA, clock);

        var sentByA = await dispatcherA.DispatchPendingAsync(10, CancellationToken.None);

        var all = providerA.Sent.Concat(providerB.Sent).ToList();
        Assert.Equal(rows.Count, all.Count);
        Assert.Equal(rows.Count, all.Distinct().Count());
        Assert.Equal(1, sentByA);
        Assert.Equal(rows.Count - 1, providerB.Sent.Count);

        var stored = await ReadAllAsync(db, tenantId);
        Assert.All(stored.Values, m =>
        {
            Assert.Equal(OutboxStatus.Sent, m.Status);
            Assert.Equal(1, m.AttemptCount);
            Assert.Null(m.ClaimToken);
        });
    }

    [Fact]
    public async Task ClaimedRow_IsInvisibleToASecondDispatcher()
    {
        var (db, tenantId, rows) = await SeedAsync(1);
        using var _ = db;
        await using var secondContext = db.CreateContextForTenant(tenantId);
        var clock = new TestClock(Now);
        var providerB = new ScriptedProvider();
        var dispatcherB = new MessageDispatcher(secondContext, providerB, providerB, clock);

        var observedStatus = OutboxStatus.Pending;
        var providerA = new ScriptedProvider
        {
            OnSend = async message =>
            {
                await using var probe = db.CreateContextForTenant(tenantId);
                observedStatus = (await probe.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id)).Status;
                Assert.Equal(0, await dispatcherB.DispatchPendingAsync(10, CancellationToken.None));
            },
        };

        await new MessageDispatcher(db.Context, providerA, providerA, clock).DispatchPendingAsync(10, CancellationToken.None);

        Assert.Equal(OutboxStatus.Sending, observedStatus);
        Assert.Empty(providerB.Sent);
        Assert.Equal(rows[0].Id, Assert.Single(providerA.Sent));
    }

    [Fact]
    public async Task OutcomeIsPersistedPerMessage_AShutdownMidBatchNeverResendsEarlierMessages()
    {
        var (db, tenantId, rows) = await SeedAsync(3);
        using var _ = db;
        var clock = new TestClock(Now);

        var attempted = new List<Guid>();
        var provider = new ScriptedProvider
        {
            // The second send is interrupted by SIGTERM (the host cancels the run).
            OnSend = message =>
            {
                attempted.Add(message.Id);
                return attempted.Count == 2
                    ? throw new OperationCanceledException("host is stopping")
                    : Task.CompletedTask;
            },
        };
        var dispatcher = new MessageDispatcher(db.Context, provider, provider, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchPendingAsync(10, CancellationToken.None));

        var delivered = attempted[0];
        var interrupted = attempted[1];
        var untouched = rows.Select(r => r.Id).Except(attempted).Single();
        var afterCrash = await ReadAllAsync(db, tenantId);
        Assert.Equal(OutboxStatus.Sent, afterCrash[delivered].Status);        // saved right after its send
        Assert.Equal(OutboxStatus.Sending, afterCrash[interrupted].Status);   // interrupted mid-send
        Assert.Equal(OutboxStatus.Pending, afterCrash[untouched].Status);     // never touched

        // After the restart, within the stale window: the interrupted row is left alone.
        var restarted = new ScriptedProvider();
        await using (var next = db.CreateContextForTenant(tenantId))
        {
            await new MessageDispatcher(next, restarted, restarted, clock).DispatchPendingAsync(10, CancellationToken.None);
        }

        Assert.Equal(new[] { untouched }, restarted.Sent);

        // Past the stale window: held for review (possibly delivered), still not resent.
        clock.Advance(MessageDispatcher.StaleClaimAfter + TimeSpan.FromMinutes(1));
        await using (var later = db.CreateContextForTenant(tenantId))
        {
            await new MessageDispatcher(later, restarted, restarted, clock).DispatchPendingAsync(10, CancellationToken.None);
        }

        Assert.Equal(new[] { untouched }, restarted.Sent);
        var final = await ReadAllAsync(db, tenantId);
        Assert.Equal(OutboxStatus.Sent, final[delivered].Status);
        Assert.Equal(OutboxStatus.AwaitingReview, final[interrupted].Status);
        Assert.Equal(MessageDispatcher.StaleClaimReason, final[interrupted].FailureReason);
        Assert.Equal(1, final[interrupted].AttemptCount);
        Assert.Null(final[interrupted].ClaimToken);
    }

    [Fact]
    public async Task PoisonRow_WithAnUnknownOwnerType_IsQuarantined_AndTheBatchContinues()
    {
        var (db, tenantId, rows) = await SeedAsync(3);
        using var _ = db;
        // An old release / manual fix wrote a value the current enum cannot read: materialising
        // this row throws. The audit saw it stop ALL mail every 30 s.
        var poisonKind = "poison_kind";
        await db.Context.OutboxMessages.Where(m => m.Id == rows[1].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Kind, poisonKind));
        await db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE outbox_messages SET \"OwnerType\" = 'Supplier' WHERE \"Kind\" = 'poison_kind'");

        var provider = new ScriptedProvider();
        var sent = await new MessageDispatcher(db.Context, provider, provider, new TestClock(Now))
            .DispatchPendingAsync(10, CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal(new[] { rows[0].Id, rows[2].Id }.OrderBy(id => id), provider.Sent.OrderBy(id => id));

        await using var probe = db.CreateContextForTenant(tenantId);
        var poison = await probe.OutboxMessages.AsNoTracking()
            .Where(m => m.Id == rows[1].Id)
            .Select(m => new { m.Status, m.FailureReason, m.ClaimToken })
            .SingleAsync();
        Assert.Equal(OutboxStatus.Failed, poison.Status);
        Assert.StartsWith("Ongeldige outboxrij", poison.FailureReason);
        Assert.Null(poison.ClaimToken);

        // The next run does not pick it up again.
        var again = new ScriptedProvider();
        await new MessageDispatcher(probe, again, again, new TestClock(Now)).DispatchPendingAsync(10, CancellationToken.None);
        Assert.Empty(again.Sent);
    }

    /// <summary>Throws when the permanent-failure fallback row is saved (simulates a constraint error).</summary>
    private sealed class FailFallbackInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxMessage>()
                .Any(e => e.State == EntityState.Added && e.Entity.FallbackOfMessageId != null))
            {
                throw new DbUpdateException("value too long for type character varying(300)");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task FailingPermanentFailureAlert_DoesNotPoisonTheBatch()
    {
        var (db, tenantId, rows) = await SeedAsync(2, new FailFallbackInsertInterceptor());
        using var _ = db;
        await db.Context.OutboxMessages.Where(m => m.Id == rows[0].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AttemptCount, 4));
        db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OwnerType = MessageOwnerType.Customer, OwnerId = rows[0].OwnerId,
            FallbackChannel = MessageChannel.Sms, PhoneNumber = "+32470000000",
        });
        await db.Context.SaveChangesAsync();

        var provider = new ScriptedProvider
        {
            OnSend = message => message.Id == rows[0].Id
                ? throw new InvalidOperationException("SMTP-verzending is mislukt.")
                : Task.CompletedTask,
        };
        await new MessageDispatcher(db.Context, provider, provider, new TestClock(Now)).DispatchPendingAsync(10, CancellationToken.None);

        var stored = await ReadAllAsync(db, tenantId);
        Assert.Equal(OutboxStatus.Failed, stored[rows[0].Id].Status);
        Assert.Equal(5, stored[rows[0].Id].AttemptCount);
        Assert.Equal(OutboxStatus.Sent, stored[rows[1].Id].Status);
        Assert.Equal(2, stored.Count); // the fallback row was rolled back, nothing half-written

        // Not resent on the next run either.
        var again = new ScriptedProvider();
        await new MessageDispatcher(db.Context, again, again, new TestClock(Now)).DispatchPendingAsync(10, CancellationToken.None);
        Assert.Empty(again.Sent);
    }

    [Fact]
    public async Task SmsRow_WithoutAConfiguredSmsProvider_IsSuppressedWithAReason_NotRetried()
    {
        var (db, tenantId, rows) = await SeedAsync(1);
        using var _ = db;
        await db.Context.OutboxMessages.Where(m => m.Id == rows[0].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Channel, MessageChannel.Sms));

        var email = new ScriptedProvider();
        await new MessageDispatcher(db.Context, email, new UnconfiguredSmsProvider(), new TestClock(Now))
            .DispatchPendingAsync(10, CancellationToken.None);

        var stored = (await ReadAllAsync(db, tenantId))[rows[0].Id];
        Assert.Equal(OutboxStatus.Suppressed, stored.Status);
        Assert.Equal(MessageDispatcher.ChannelNotConfiguredReason, stored.FailureReason);
        Assert.Null(stored.NextAttemptAt);
    }
}
