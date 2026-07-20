namespace TransportationService.Api.Modules.Operations.Services;

public interface IAlertSyncService
{
    /// <summary>
    /// Recomputes the operational alert projection for the current tenant and upserts by dedupe
    /// key: new conditions create alerts, persisting conditions bump LastSeenAt, disappeared
    /// conditions auto-resolve. Idempotent — safe to run on every overview refresh.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken);
}
