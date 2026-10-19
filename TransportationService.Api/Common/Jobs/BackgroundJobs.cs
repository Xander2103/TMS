using System.Collections.Concurrent;

namespace TransportationService.Api.Common.Jobs;

/// <summary>Stable job names: they key the advisory lock AND the heartbeat entry.</summary>
public static class JobNames
{
    public const string StartupSync = "startup-sync";
    public const string OutboxDispatch = "outbox-dispatch";
    public const string EmploymentEndSweep = "employment-end-sweep";
    public const string TaskSweep = "task-sweep";
    public const string ExpirySweep = "expiry-sweep";
    public const string InventorySweep = "inventory-sweep";
    public const string NotificationMaintenance = "notification-maintenance";
    public const string TokenRetention = "token-retention";
    public const string GdprRetention = "gdpr-retention";
    public const string AttendanceSweep = "attendance-sweep";
    public const string CalendarSync = "calendar-sync";
    public const string AutomaticBackup = "automatic-backup";
}

public enum JobRunOutcome
{
    Succeeded,
    Failed,

    /// <summary>Another instance held the job's lock; this instance did nothing.</summary>
    SkippedLockHeld,
}

/// <summary>Last-known state of one background job in THIS process (in-memory by design).</summary>
public sealed record JobHeartbeat(
    string Name,
    DateTime? LastSuccessUtc,
    DateTime? LastFailureUtc,
    string? LastError,
    DateTime? LastSkippedUtc,
    JobRunOutcome? LastOutcome,
    DateTime? LastRunUtc);

/// <summary>
/// H1/BG-08: per-job heartbeat surfaced in <c>/health/details</c>. In-memory: it describes what
/// this process did since it started, which is what an operator needs to spot a dead or failing
/// loop. Error text is the exception TYPE only — never a message that could carry data.
/// </summary>
public sealed class JobHeartbeatRegistry
{
    private readonly ConcurrentDictionary<string, JobHeartbeat> _beats = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public JobHeartbeatRegistry(TimeProvider clock)
    {
        _clock = clock;
    }

    public void RecordSuccess(string name) => Update(name, beat => beat with
    {
        LastSuccessUtc = Now, LastOutcome = JobRunOutcome.Succeeded, LastRunUtc = Now,
    });

    public void RecordFailure(string name, Exception exception) => Update(name, beat => beat with
    {
        LastFailureUtc = Now, LastError = exception.GetType().Name, LastOutcome = JobRunOutcome.Failed, LastRunUtc = Now,
    });

    public void RecordSkipped(string name) => Update(name, beat => beat with
    {
        LastSkippedUtc = Now, LastOutcome = JobRunOutcome.SkippedLockHeld, LastRunUtc = Now,
    });

    public IReadOnlyList<JobHeartbeat> Snapshot() =>
        _beats.Values.OrderBy(beat => beat.Name, StringComparer.Ordinal).ToList();

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private void Update(string name, Func<JobHeartbeat, JobHeartbeat> change) =>
        _beats.AddOrUpdate(
            name,
            key => change(new JobHeartbeat(key, null, null, null, null, null, null)),
            (_, current) => change(current));
}

/// <summary>
/// Runs one tick of a background job under its cross-process lock and records the heartbeat.
/// Exceptions are recorded and RETHROWN so each hosted service keeps its own log line and
/// retry-next-interval behaviour.
/// </summary>
public sealed class BackgroundJobRunner
{
    private readonly IJobLock _jobLock;
    private readonly JobHeartbeatRegistry _heartbeats;
    private readonly ILogger<BackgroundJobRunner> _logger;

    public BackgroundJobRunner(IJobLock jobLock, JobHeartbeatRegistry heartbeats, ILogger<BackgroundJobRunner> logger)
    {
        _jobLock = jobLock;
        _heartbeats = heartbeats;
        _logger = logger;
    }

    public async Task<JobRunOutcome> RunExclusiveAsync(
        string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        IAsyncDisposable? handle;
        try
        {
            handle = await _jobLock.TryAcquireAsync(name, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _heartbeats.RecordFailure(name, exception);
            throw;
        }

        if (handle is null)
        {
            _heartbeats.RecordSkipped(name);
            _logger.LogInformation("Job {Job} skipped: another instance holds its lock.", name);
            return JobRunOutcome.SkippedLockHeld;
        }

        await using (handle)
        {
            try
            {
                await work(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _heartbeats.RecordFailure(name, exception);
                throw;
            }
        }

        _heartbeats.RecordSuccess(name);
        return JobRunOutcome.Succeeded;
    }

    /// <summary>Heartbeat-only variant for jobs that coordinate per row instead of per run
    /// (the outbox claims each message), so they need no global lock.</summary>
    public async Task RunTrackedAsync(string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await work(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _heartbeats.RecordFailure(name, exception);
            throw;
        }

        _heartbeats.RecordSuccess(name);
    }
}

public static class BackgroundJobScopeExtensions
{
    /// <summary>
    /// One tick of a hosted service: a fresh DI scope, the job's cross-process lock and a
    /// heartbeat. Hosts that do not register <see cref="BackgroundJobRunner"/> (narrow test hosts)
    /// simply run the work.
    /// </summary>
    public static async Task RunJobAsync(
        this IServiceScopeFactory scopeFactory, string name,
        Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetService<BackgroundJobRunner>();
        if (runner is null)
        {
            await work(scope.ServiceProvider, cancellationToken);
            return;
        }

        await runner.RunExclusiveAsync(name, token => work(scope.ServiceProvider, token), cancellationToken);
    }

    /// <summary>As <see cref="RunJobAsync"/> but without the lock (per-row coordinated jobs).</summary>
    public static async Task RunTrackedJobAsync(
        this IServiceScopeFactory scopeFactory, string name,
        Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetService<BackgroundJobRunner>();
        if (runner is null)
        {
            await work(scope.ServiceProvider, cancellationToken);
            return;
        }

        await runner.RunTrackedAsync(name, token => work(scope.ServiceProvider, token), cancellationToken);
    }

    public static IServiceCollection AddBackgroundJobCoordination(this IServiceCollection services)
    {
        services.AddSingleton<IJobLock, DatabaseJobLock>();
        services.AddSingleton<JobHeartbeatRegistry>();
        services.AddSingleton<BackgroundJobRunner>();
        return services;
    }
}
