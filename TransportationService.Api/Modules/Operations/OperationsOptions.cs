namespace TransportationService.Api.Modules.Operations;

/// <summary>Operations control-center settings, bound to the "Operations" configuration section.</summary>
public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    /// <summary>Floor for <see cref="AlertSyncIntervalSeconds"/>; smaller values are clamped.</summary>
    public const int MinimumAlertSyncIntervalSeconds = 10;

    /// <summary>
    /// Seconds between two background alert-reconciliation ticks (PERF-HIGH 4). The overview GET
    /// is read-only; <c>OperationalAlertSyncHostedService</c> and the manual
    /// <c>POST /api/operations/alerts/sync</c> are the only writers of <c>operational_alerts</c>.
    /// </summary>
    public int AlertSyncIntervalSeconds { get; set; } = 60;
}
