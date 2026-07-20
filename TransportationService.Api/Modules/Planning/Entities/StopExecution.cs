using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Planning.Entities;

/// <summary>
/// Controlled stop lifecycle. Skipped means the stop was never visited (pre-arrival only);
/// Failed means an attempt was made but nothing could be handled; PartiallyCompleted means
/// part of the goods were handled. The valid transitions live in <see cref="StopStatusMachine"/>.
/// </summary>
public enum StopExecutionStatus
{
    Planned,
    EnRoute,
    Arrived,
    Loading,
    Loaded,
    Unloading,
    Completed,
    PartiallyCompleted,
    Failed,
    Skipped,
}

/// <summary>
/// Execution progress of one order stop within one trip (driver workflow). Rows are created
/// lazily on the first event; stops without a row are Planned. The POD is stored as an opaque
/// storage key (upload endpoints follow the shared attachment architecture); external scanning
/// hardware is out of scope.
/// </summary>
public class StopExecution : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderStopId { get; set; }

    public StopExecutionStatus Status { get; set; } = StopExecutionStatus.Planned;

    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>When the vehicle actually left the stop; stamped on every terminal transition.</summary>
    public DateTime? DepartedAt { get; set; }

    /// <summary>Mandatory explanation when the arrival was after the latest allowed/confirmed/planned bound.</summary>
    public string? LateArrivalReason { get; set; }

    /// <summary>Mandatory explanation for Skipped, Failed and PartiallyCompleted.</summary>
    public string? StatusReason { get; set; }

    /// <summary>Opaque storage key of the proof-of-delivery document/photo.</summary>
    public string? PodPath { get; set; }

    /// <summary>Name of the person who signed at the stop.</summary>
    public string? PodSignedBy { get; set; }

    public string? Remarks { get; set; }
}

/// <summary>
/// Immutable trail of every stop-status change: who moved the stop from where to where, when
/// and why. Written inside the same transaction as the transition itself.
/// </summary>
public class StopStatusHistory : AuditableTenantEntity
{
    public Guid StopExecutionId { get; set; }

    public StopExecutionStatus FromStatus { get; set; }
    public StopExecutionStatus ToStatus { get; set; }

    public DateTime OccurredAt { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>The reason supplied with the transition (skip/failure/partial/late arrival).</summary>
    public string? Reason { get; set; }

    /// <summary>Client idempotency key of the transition request (offline replay protection).</summary>
    public Guid? ClientRequestId { get; set; }
}
