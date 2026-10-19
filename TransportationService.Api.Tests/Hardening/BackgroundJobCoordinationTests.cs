using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Common.Jobs;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Integrations.Entities;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// H7: background jobs run under a cross-process lock (a second instance skips the run) and
/// leave a heartbeat; the honest calendar provider never marks anything Synced.
/// </summary>
public class BackgroundJobCoordinationTests
{
    /// <summary>Simulates another instance holding the PostgreSQL advisory lock.</summary>
    private sealed class HeldElsewhereLock : IJobLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);

        public Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class CountingLock : IJobLock
    {
        public int Acquired { get; private set; }
        public int Released { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
        {
            Acquired += 1;
            return Task.FromResult<IAsyncDisposable?>(new Handle(this));
        }

        public Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) =>
            TryAcquireAsync(name, cancellationToken);

        private sealed class Handle(CountingLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released += 1;
                return ValueTask.CompletedTask;
            }
        }
    }

    private static BackgroundJobRunner Runner(IJobLock jobLock, JobHeartbeatRegistry heartbeats) =>
        new(jobLock, heartbeats, NullLogger<BackgroundJobRunner>.Instance);

    [Fact]
    public async Task SecondInstance_SkipsTheRun_WhenTheLockIsHeldElsewhere()
    {
        var heartbeats = new JobHeartbeatRegistry(TimeProvider.System);
        var ran = false;

        var outcome = await Runner(new HeldElsewhereLock(), heartbeats)
            .RunExclusiveAsync(JobNames.EmploymentEndSweep, _ => { ran = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(JobRunOutcome.SkippedLockHeld, outcome);
        Assert.False(ran);
        var beat = Assert.Single(heartbeats.Snapshot());
        Assert.Equal(JobRunOutcome.SkippedLockHeld, beat.LastOutcome);
        Assert.Null(beat.LastSuccessUtc);
    }

    [Fact]
    public async Task SuccessfulRun_ReleasesTheLock_AndRecordsTheHeartbeat()
    {
        var heartbeats = new JobHeartbeatRegistry(TimeProvider.System);
        var jobLock = new CountingLock();

        var outcome = await Runner(jobLock, heartbeats)
            .RunExclusiveAsync(JobNames.TaskSweep, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(JobRunOutcome.Succeeded, outcome);
        Assert.Equal(1, jobLock.Acquired);
        Assert.Equal(1, jobLock.Released);
        Assert.NotNull(Assert.Single(heartbeats.Snapshot()).LastSuccessUtc);
    }

    [Fact]
    public async Task FailingRun_ReleasesTheLock_RecordsTheFailureType_AndRethrows()
    {
        var heartbeats = new JobHeartbeatRegistry(TimeProvider.System);
        var jobLock = new CountingLock();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(jobLock, heartbeats)
            .RunExclusiveAsync(JobNames.ExpirySweep, _ => throw new InvalidOperationException("tenant 42 has bad data"), CancellationToken.None));

        Assert.Equal(1, jobLock.Released);
        var beat = Assert.Single(heartbeats.Snapshot());
        Assert.Equal(JobRunOutcome.Failed, beat.LastOutcome);
        Assert.Equal(nameof(InvalidOperationException), beat.LastError);
    }

    [Fact]
    public async Task ScopeExtension_UsesTheRegisteredRunner_AndFallsBackWithoutOne()
    {
        var withRunner = new ServiceCollection()
            .AddSingleton<IJobLock>(new HeldElsewhereLock())
            .AddSingleton(new JobHeartbeatRegistry(TimeProvider.System))
            .AddSingleton<BackgroundJobRunner>()
            .AddLogging()
            .BuildServiceProvider();
        var ran = false;
        await withRunner.GetRequiredService<IServiceScopeFactory>()
            .RunJobAsync(JobNames.GdprRetention, (_, _) => { ran = true; return Task.CompletedTask; }, CancellationToken.None);
        Assert.False(ran);

        var bare = new ServiceCollection().BuildServiceProvider();
        await bare.GetRequiredService<IServiceScopeFactory>()
            .RunJobAsync(JobNames.GdprRetention, (_, _) => { ran = true; return Task.CompletedTask; }, CancellationToken.None);
        Assert.True(ran);
    }

    [Fact]
    public async Task DatabaseJobLock_IsANoOpOnNonPostgresProviders()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection()
            .AddDbContext<TransportationDbContext>(options => options.UseSqlite(connection))
            .AddSingleton<TransportationService.Api.Common.Persistence.ITenantQueryFilterAccessor>(
                new TransportationService.Api.Common.Persistence.FixedTenantQueryFilterAccessor(null))
            .BuildServiceProvider();
        var jobLock = new DatabaseJobLock(services.GetRequiredService<IServiceScopeFactory>());

        await using var first = await jobLock.TryAcquireAsync(JobNames.StartupSync, CancellationToken.None);
        await using var second = await jobLock.AcquireAsync(JobNames.StartupSync, TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void LockKeys_AreStableAndDistinctPerJob()
    {
        Assert.Equal(DatabaseJobLock.KeyFor(JobNames.AutomaticBackup), DatabaseJobLock.KeyFor("automatic-backup"));
        var keys = typeof(JobNames).GetFields()
            .Select(field => DatabaseJobLock.KeyFor((string)field.GetValue(null)!))
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public async Task NotConfiguredCalendarProvider_LeavesItemsPending_NeverSynced()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new TransportationService.Api.Modules.Tenancy.Entities.Tenant
        {
            Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow,
        });
        db.Context.CalendarSyncItems.Add(new CalendarSyncItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EventType = "absence", EntityId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(), Title = "Verlof", StartDate = new DateOnly(2026, 11, 2), EndDate = new DateOnly(2026, 11, 3),
            Operation = CalendarSyncOperation.Upsert, Status = CalendarSyncStatus.Pending,
        });
        await db.Context.SaveChangesAsync();

        var processed = await new CalendarSyncProcessor(db.Context, new NotConfiguredCalendarProvider(), TimeProvider.System)
            .ProcessPendingAsync(50, CancellationToken.None);

        Assert.Equal(0, processed);
        var item = await db.Context.CalendarSyncItems.AsNoTracking().SingleAsync();
        Assert.Equal(CalendarSyncStatus.Pending, item.Status);
        Assert.Null(item.ExternalEventId);
        Assert.Equal(0, item.AttemptCount);
    }
}
