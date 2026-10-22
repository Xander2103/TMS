namespace TransportationService.Api.Modules.Operations.Services;

/// <summary>
/// Row-level outcome of one reconciliation run. Each counter is an exact bound on the statements
/// that run issued: one INSERT per <see cref="Created"/>, one UPDATE per <see cref="Updated"/>,
/// <see cref="Reopened"/>, <see cref="Refreshed"/> and <see cref="Resolved"/> row; an
/// <see cref="Unchanged"/> row is never written.
/// </summary>
public sealed record AlertSyncResult(int Created, int Updated, int Reopened, int Refreshed, int Resolved, int Unchanged)
{
    public static readonly AlertSyncResult Empty = new(0, 0, 0, 0, 0, 0);

    /// <summary>Rows written by this run; zero in steady state.</summary>
    public int Writes => Created + Updated + Reopened + Refreshed + Resolved;

    public AlertSyncResult Add(AlertSyncResult other) => new(
        Created + other.Created, Updated + other.Updated, Reopened + other.Reopened,
        Refreshed + other.Refreshed, Resolved + other.Resolved, Unchanged + other.Unchanged);
}

public interface IAlertSyncService
{
    /// <summary>
    /// Reconciles the operational alert projection of <paramref name="tenantId"/> with the current
    /// conditions, upserting by dedupe key: a new condition inserts, a changed one (severity, title,
    /// message) or a returning one (reopen) updates, a disappeared one auto-resolves, and an
    /// unchanged persisting condition is left untouched apart from a LastSeenAt refresh at most once
    /// per <see cref="AlertSyncService.LastSeenRefreshInterval"/>. Idempotent and tenant-explicit, so
    /// the background job can run it outside a request scope.
    /// </summary>
    Task<AlertSyncResult> SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Request-scoped convenience: <see cref="SyncTenantAsync"/> for the current tenant.</summary>
    Task<AlertSyncResult> SyncAsync(CancellationToken cancellationToken);
}
