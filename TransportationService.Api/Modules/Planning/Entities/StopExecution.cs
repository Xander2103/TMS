using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Planning.Entities;

public enum StopExecutionStatus
{
    Pending,
    Arrived,
    Completed,
    Skipped,
}

/// <summary>
/// Execution progress of one order stop within one trip (driver workflow). Rows are created
/// lazily on the first event; stops without a row are Pending. The POD is stored as an opaque
/// storage key (upload endpoints follow the shared attachment architecture); external scanning
/// hardware is out of scope.
/// </summary>
public class StopExecution : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderStopId { get; set; }

    public StopExecutionStatus Status { get; set; } = StopExecutionStatus.Pending;

    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Opaque storage key of the proof-of-delivery document/photo.</summary>
    public string? PodPath { get; set; }

    /// <summary>Name of the person who signed at the stop.</summary>
    public string? PodSignedBy { get; set; }

    public string? Remarks { get; set; }
}
