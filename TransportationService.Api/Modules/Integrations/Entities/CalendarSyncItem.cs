using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Integrations.Entities;

public enum CalendarSyncStatus
{
    Pending,
    Synced,
    Failed,
    Cancelled,
}

public enum CalendarSyncOperation
{
    Upsert,
    Cancel,
}

/// <summary>
/// One calendar fact awaiting (or done) mirroring to an external calendar. One row per
/// (event type, entity): requeues update the row, so the external event id survives and
/// upserts stay idempotent. The TMS is always the source of truth.
/// </summary>
public class CalendarSyncItem : AuditableTenantEntity
{
    public string EventType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid EmployeeId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Title { get; set; } = string.Empty;

    public CalendarSyncOperation Operation { get; set; } = CalendarSyncOperation.Upsert;
    public CalendarSyncStatus Status { get; set; } = CalendarSyncStatus.Pending;

    public string? ExternalEventId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? ErrorDetail { get; set; }
}
