using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// One piece of operational work inside a dossier. Deliberately thin: for transport-shaped
/// work (type.HasStops) the linked TransportOrder IS the execution record — stops, cargo,
/// pricing, scanning and POD live there and are referenced, never copied. A field that
/// exists on TransportOrder must not be added here. Standalone activities (crane on site,
/// plateau, storage) carry only the minimal shared scalars; their own execution models
/// arrive in later waves. Transport activities derive their status live from the order.
/// T-H7 (2026-10-16): a STANDALONE activity is its own operational unit — it carries an
/// <see cref="ExecutionStatus"/> (Planned → InProgress → Completed, or Cancelled with a reason)
/// and, for planning-relevant types, the minimal execution facts of on-site work: start time,
/// site, work description, driver and vehicle. Still no stops, no cargo, no order.
/// </summary>
public class DossierActivity : AuditableTenantEntity
{
    public Guid DossierId { get; set; }

    public Guid ActivityTypeId { get; set; }
    public ActivityType? ActivityType { get; set; }

    /// <summary>1..n within the dossier; rebuilt on reorder.</summary>
    public int Sequence { get; set; }

    /// <summary>Optional free label ("Kraan Nexans site B").</summary>
    public string? Label { get; set; }

    /// <summary>The execution record for HasStops types; SetNull so order deletion never cascades here.</summary>
    public Guid? LinkedTransportOrderId { get; set; }

    /// <summary>
    /// Operational accompaniment within the SAME dossier (plateau → crane transport);
    /// same-dossier rule enforced by the service.
    /// </summary>
    public Guid? LinkedActivityId { get; set; }

    /// <summary>Standalone activities only; HasStops types read their dates from the order.</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Only when the type AllowsDuration; validated >= 0.</summary>
    public decimal? DurationHours { get; set; }

    public string? Notes { get; set; }

    // ------------------------------------------------------------ standalone execution (T-H7)

    /// <summary>
    /// Standalone activities only (an order-backed activity reads its status from the order).
    /// Completed/Cancelled resolve the activity for dossier auto-confirmation; only a Completed
    /// planning-relevant activity is invoice-ready.
    /// </summary>
    public DossierActivityExecutionStatus ExecutionStatus { get; set; } = DossierActivityExecutionStatus.Planned;

    /// <summary>Local start time on <see cref="PlannedDate"/>; the end is derived (start + duration).</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Site / work address as free text (on-site work has no stop).</summary>
    public string? SiteAddress { get; set; }

    public string? WorkDescription { get; set; }

    /// <summary>Operator / driver doing the work (planning-relevant standalone types only).</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Vehicle / equipment used (e.g. the crane truck).</summary>
    public Guid? VehicleId { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public Guid? CompletedByUserId { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Guid? CancelledByUserId { get; set; }

    public string? CancellationReason { get; set; }
}

/// <summary>Execution state of a standalone dossier activity; stored as a string column.</summary>
public enum DossierActivityExecutionStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled,
}
