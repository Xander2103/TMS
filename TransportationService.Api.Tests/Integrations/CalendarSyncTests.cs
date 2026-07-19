using TransportationService.Api.Modules.Integrations.Entities;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Integrations;

/// <summary>
/// Wave 12: the Outlook/Exchange boundary — a durable sync queue with idempotent upserts,
/// external event ids, retries and cancellation, processed against the development fake
/// adapter. The TMS stays the source of truth; no Microsoft credentials are involved.
/// </summary>
public class CalendarSyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, QueueingCalendarSyncService Queue, CalendarSyncProcessor Processor,
        FakeCalendarProvider Provider, TestClock Clock, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var queue = new QueueingCalendarSyncService(db.Context, new DevTenantContext(tenantId), clock);
        var provider = new FakeCalendarProvider();
        var processor = new CalendarSyncProcessor(db.Context, provider, clock);
        return new Harness(db, queue, processor, provider, clock, tenantId);
    }

    private static CalendarSyncEvent LeaveEvent(Guid entityId, DateOnly? start = null) =>
        new("leave_approved", entityId, Guid.NewGuid(), start ?? new(2026, 8, 3), new(2026, 8, 7), "Afwezigheid: Vacation");

    [Fact]
    public async Task Queue_IsIdempotentPerEntity_AndUpdatesOnRequeue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var entityId = Guid.NewGuid();

        await h.Queue.QueueAsync(LeaveEvent(entityId), CancellationToken.None);
        await h.Queue.QueueAsync(LeaveEvent(entityId, new(2026, 8, 10)), CancellationToken.None);

        var item = h.Db.Context.CalendarSyncItems.Single();
        Assert.Equal(new DateOnly(2026, 8, 10), item.StartDate);
        Assert.Equal(CalendarSyncStatus.Pending, item.Status);
        Assert.Equal(CalendarSyncOperation.Upsert, item.Operation);
    }

    [Fact]
    public async Task Processor_Syncs_AssignsExternalId()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Queue.QueueAsync(LeaveEvent(Guid.NewGuid()), CancellationToken.None);

        var processed = await h.Processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, processed);
        var item = h.Db.Context.CalendarSyncItems.Single();
        Assert.Equal(CalendarSyncStatus.Synced, item.Status);
        Assert.NotNull(item.ExternalEventId);
        Assert.NotNull(item.LastSyncAt);
        Assert.Single(h.Provider.Upserts);
    }

    [Fact]
    public async Task Update_AfterSync_ReusesExternalEventId()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var entityId = Guid.NewGuid();
        await h.Queue.QueueAsync(LeaveEvent(entityId), CancellationToken.None);
        await h.Processor.ProcessPendingAsync(10, CancellationToken.None);
        var externalId = h.Db.Context.CalendarSyncItems.Single().ExternalEventId;

        // Dates change: the item goes Pending again and the provider upserts the SAME event.
        await h.Queue.QueueAsync(LeaveEvent(entityId, new(2026, 9, 1)), CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Pending, h.Db.Context.CalendarSyncItems.Single().Status);
        await h.Processor.ProcessPendingAsync(10, CancellationToken.None);

        var item = h.Db.Context.CalendarSyncItems.Single();
        Assert.Equal(externalId, item.ExternalEventId);
        Assert.Equal(2, h.Provider.Upserts.Count);
        Assert.Equal(CalendarSyncStatus.Synced, item.Status);
    }

    [Fact]
    public async Task Cancel_MarksAndCallsProvider()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var entityId = Guid.NewGuid();
        await h.Queue.QueueAsync(LeaveEvent(entityId), CancellationToken.None);
        await h.Processor.ProcessPendingAsync(10, CancellationToken.None);

        await h.Queue.CancelAsync("leave_approved", entityId, CancellationToken.None);
        await h.Processor.ProcessPendingAsync(10, CancellationToken.None);

        var item = h.Db.Context.CalendarSyncItems.Single();
        Assert.Equal(CalendarSyncStatus.Cancelled, item.Status);
        Assert.Single(h.Provider.Cancellations);

        // Cancelling something never synced (or unknown) is a safe no-op.
        await h.Queue.CancelAsync("leave_approved", Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task Failures_Retry_ThenFail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Queue.QueueAsync(LeaveEvent(Guid.NewGuid()), CancellationToken.None);
        h.Provider.Fail = true;

        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            await h.Processor.ProcessPendingAsync(10, CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromHours(2));
        }

        var item = h.Db.Context.CalendarSyncItems.Single();
        Assert.Equal(CalendarSyncStatus.Failed, item.Status);
        Assert.Equal(5, item.AttemptCount);
        Assert.NotNull(item.ErrorDetail);

        // A retry after recovery drains it.
        h.Provider.Fail = false;
        item.Status = CalendarSyncStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptAt = null;
        await h.Db.Context.SaveChangesAsync();
        await h.Processor.ProcessPendingAsync(10, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Synced, h.Db.Context.CalendarSyncItems.Single().Status);
    }
}
