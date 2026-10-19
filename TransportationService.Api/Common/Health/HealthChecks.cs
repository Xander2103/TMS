using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportationService.Api.Common.Jobs;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Security;

namespace TransportationService.Api.Common.Health;

/// <summary>
/// Health-check tags. ONLY <see cref="Ready"/> checks gate <c>/health/ready</c> (and therefore
/// the deploy check and the load balancer); every other tag is informational and shows up in the
/// authenticated <c>/health/details</c> view only. Other modules register their own checks with
/// these tags via additional <c>AddHealthChecks().AddCheck&lt;T&gt;(name, tags: …)</c> calls.
/// </summary>
public static class HealthTags
{
    public const string Ready = "ready";
    public const string Peppol = "peppol";
    public const string Backup = "backup";
    public const string Outbox = "outbox";
    public const string Jobs = "jobs";
    public const string Integrations = "integrations";
}

/// <summary>Readiness: the database answers <c>SELECT 1</c> within a short timeout.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly TransportationDbContext _db;

    public DatabaseHealthCheck(TransportationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return HealthCheckResult.Healthy("Database bereikbaar.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Never the exception text: it can carry host names or connection details.
            return HealthCheckResult.Unhealthy("Database niet bereikbaar.");
        }
    }
}

/// <summary>Readiness: the schema matches the code (0 pending EF migrations).</summary>
public sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly TransportationDbContext _db;

    public PendingMigrationsHealthCheck(TransportationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql())
        {
            // Migrations are PostgreSQL-specific; a test host builds its schema with EnsureCreated.
            return HealthCheckResult.Healthy("Niet van toepassing voor deze databaseprovider.");
        }

        try
        {
            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            return pending == 0
                ? HealthCheckResult.Healthy("Databaseschema is up-to-date.")
                : HealthCheckResult.Unhealthy($"{pending} openstaande databasemigratie(s).");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Migratiestatus kon niet worden bepaald.");
        }
    }
}

/// <summary>Readiness (H5): the configured column-encryption key ring decrypts the database's data.</summary>
public sealed class ColumnEncryptionHealthCheck : IHealthCheck
{
    private readonly TransportationDbContext _db;
    private readonly TimeProvider _clock;

    public ColumnEncryptionHealthCheck(TransportationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ColumnEncryptionVerifier.VerifyAsync(_db, allowWrite: false, _clock, cancellationToken);
            return result.Healthy
                ? HealthCheckResult.Healthy(result.Description)
                : HealthCheckResult.Unhealthy(result.Description);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Kolomversleuteling kon niet worden gecontroleerd.");
        }
    }
}

/// <summary>
/// Details only: outbox backlog and failures (the operator signal for "mail is broken"). Counts
/// only, never recipients or bodies. Degraded when mail failed in the last 24 h, Pending mail is
/// older than an hour, or interrupted sends wait for review.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly TransportationDbContext _db;
    private readonly TimeProvider _clock;

    public OutboxHealthCheck(TransportationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var dayAgo = now.AddDays(-1);
        var hourAgo = now.AddHours(-1);
        try
        {
            var outbox = _db.OutboxMessages.AsNoTracking();
            var failed24h = await outbox.CountAsync(m => m.Status == OutboxStatus.Failed && m.UpdatedAt > dayAgo, cancellationToken);
            var stalePending = await outbox.CountAsync(m => m.Status == OutboxStatus.Pending && m.CreatedAt < hourAgo, cancellationToken);
            var sending = await outbox.CountAsync(m => m.Status == OutboxStatus.Sending, cancellationToken);
            var interrupted = await outbox.CountAsync(
                m => m.Status == OutboxStatus.AwaitingReview && m.FailureReason == MessageDispatcher.StaleClaimReason,
                cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["failedLast24h"] = failed24h,
                ["pendingOlderThan1h"] = stalePending,
                ["sending"] = sending,
                ["interruptedAwaitingReview"] = interrupted,
            };
            return failed24h > 0 || stalePending > 0 || interrupted > 0
                ? HealthCheckResult.Degraded("Uitgaande berichten vragen aandacht.", data: data)
                : HealthCheckResult.Healthy("Uitgaande berichten in orde.", data);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Outboxstatus kon niet worden bepaald.");
        }
    }
}

/// <summary>Details only (BG-08): per-job heartbeat of this process. Degraded when a job's last run failed.</summary>
public sealed class JobHeartbeatHealthCheck : IHealthCheck
{
    private readonly JobHeartbeatRegistry _heartbeats;

    public JobHeartbeatHealthCheck(JobHeartbeatRegistry heartbeats)
    {
        _heartbeats = heartbeats;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var beats = _heartbeats.Snapshot();
        var failing = beats.Where(b => b.LastOutcome == JobRunOutcome.Failed).Select(b => b.Name).ToList();
        var data = new Dictionary<string, object>
        {
            ["jobsSeen"] = beats.Count,
            ["failing"] = string.Join(",", failing),
        };
        return Task.FromResult(failing.Count > 0
            ? HealthCheckResult.Degraded($"Laatste run mislukt: {string.Join(", ", failing)}.", data: data)
            : HealthCheckResult.Healthy("Achtergrondtaken: geen fouten in de laatste run.", data));
    }
}

/// <summary>Details only: optional integrations that are deliberately disabled (CFG-04/05).</summary>
public sealed class OptionalIntegrationsHealthCheck : IHealthCheck
{
    private readonly ICalendarProvider _calendar;
    private readonly ISmsProvider _sms;

    public OptionalIntegrationsHealthCheck(ICalendarProvider calendar, ISmsProvider sms)
    {
        _calendar = calendar;
        _sms = sms;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var calendar = _calendar switch
        {
            NotConfiguredCalendarProvider => "NotConfigured",
            FakeCalendarProvider => "DevelopmentFake",
            _ => "Configured",
        };
        var sms = _sms switch
        {
            IDisabledChannelProvider => "NotConfigured",
            DevelopmentSinkProvider => "DevelopmentSink",
            _ => "Configured",
        };
        var data = new Dictionary<string, object> { ["calendar"] = calendar, ["sms"] = sms };
        return Task.FromResult(HealthCheckResult.Healthy($"Agenda: {calendar}; sms: {sms}.", data));
    }
}
