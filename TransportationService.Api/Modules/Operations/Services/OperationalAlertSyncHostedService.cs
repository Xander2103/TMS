using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Operations.Services;

/// <summary>
/// PERF-HIGH 4: the operational alert reconciliation as an explicit, bounded background job.
/// Until 2026-10 it ran inside <c>GET /api/operations/overview</c>, so every 30-second poll of
/// every open tab (hidden ones included) rewrote every open alert row — ~990 UPDATEs per request
/// were measured. Reads are now read-only; this job and the manual
/// <c>POST /api/operations/alerts/sync</c> are the only callers of the sync.
///
/// Intentional writes per tick, per tenant — nothing else is written:
///   * one INSERT per NEW condition (dedupe key not yet stored);
///   * one UPDATE per open alert whose severity, title or message CHANGED;
///   * one UPDATE per resolved alert whose condition CAME BACK (reopen of the same row);
///   * one UPDATE per open alert whose condition DISAPPEARED (system auto-resolve);
///   * one UPDATE per unchanged persisting alert, at most once per
///     <see cref="AlertSyncService.LastSeenRefreshInterval"/> (15 min), so LastSeenAt stays honest.
/// The steady state (nothing changed, inside the refresh window) therefore issues ZERO statements
/// beyond the reads. Every write is idempotent on the unique (TenantId, DedupeKey) row, so two
/// instances running the job concurrently converge on the same rows; the loser of a unique-index
/// race on a brand-new condition logs the failure for that tenant and catches up next tick.
///
/// Interval: <see cref="OperationsOptions.AlertSyncIntervalSeconds"/> (default 60 s, clamped to
/// <see cref="OperationsOptions.MinimumAlertSyncIntervalSeconds"/>). The first tick runs after a
/// short start-up delay and carries the one-time catch-up after a deploy (e.g. stale titles).
/// </summary>
public sealed class OperationalAlertSyncHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OperationalAlertSyncHostedService> _logger;
    private readonly TimeSpan _interval;

    public OperationalAlertSyncHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<OperationsOptions> options,
        ILogger<OperationalAlertSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(
            options.Value.AlertSyncIntervalSeconds, OperationsOptions.MinimumAlertSyncIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<OperationalAlertSyncWorker>();
                var run = await worker.SyncAllTenantsAsync(stoppingToken);
                if (run.Totals.Writes > 0 || run.FailedTenantCount > 0)
                {
                    _logger.LogInformation(
                        "Operational alert sync: {Tenants} tenant(s), {Created} created, {Updated} updated, "
                        + "{Reopened} reopened, {Resolved} resolved, {Refreshed} refreshed, {Failed} failed.",
                        run.TenantCount, run.Totals.Created, run.Totals.Updated, run.Totals.Reopened,
                        run.Totals.Resolved, run.Totals.Refreshed, run.FailedTenantCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Operational alert sync failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Outcome of one sweep over all active tenants.</summary>
public sealed record AlertSyncSweepResult(int TenantCount, int FailedTenantCount, AlertSyncResult Totals);

/// <summary>
/// Tenant-iterating worker behind <see cref="OperationalAlertSyncHostedService"/>. It runs in a
/// background scope: no HTTP context, so the global tenant query filter is open and the request
/// accessors are never read — every query inside <see cref="AlertSyncService"/> filters on the
/// explicit tenant id it is handed. One failing tenant never blocks the others.
/// </summary>
public sealed class OperationalAlertSyncWorker
{
    private readonly TransportationDbContext _dbContext;
    private readonly IAlertSyncService _alertSync;
    private readonly ILogger<OperationalAlertSyncWorker> _logger;

    public OperationalAlertSyncWorker(
        TransportationDbContext dbContext,
        IAlertSyncService alertSync,
        ILogger<OperationalAlertSyncWorker> logger)
    {
        _dbContext = dbContext;
        _alertSync = alertSync;
        _logger = logger;
    }

    public async Task<AlertSyncSweepResult> SyncAllTenantsAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var totals = AlertSyncResult.Empty;
        var failed = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                var result = await _alertSync.SyncTenantAsync(tenantId, cancellationToken);
                totals = totals.Add(result);
                if (result.Writes > 0)
                {
                    _logger.LogInformation(
                        "Operational alert sync for tenant {TenantId}: {Created} created, {Updated} updated, "
                        + "{Reopened} reopened, {Resolved} resolved, {Refreshed} refreshed.",
                        tenantId, result.Created, result.Updated, result.Reopened, result.Resolved, result.Refreshed);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                _logger.LogError(exception, "Operational alert sync failed for tenant {TenantId}.", tenantId);
            }
            finally
            {
                // One scope serves every tenant: drop this tenant's tracked rows so a failed
                // SaveChanges can never leak into the next tenant and memory stays bounded.
                _dbContext.ChangeTracker.Clear();
            }
        }

        return new AlertSyncSweepResult(tenantIds.Count, failed, totals);
    }
}
