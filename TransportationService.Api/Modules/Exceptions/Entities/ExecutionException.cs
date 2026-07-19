using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Exceptions.Entities;

public enum ExecutionExceptionType
{
    MissingPackage,
    WrongPackage,
    DamagedPackage,
    UnknownPackage,
    WrongRoutePackage,
    WrongStopPackage,
    IncompleteQuantity,
    UnreadableBarcode,
    LabelMissing,
    RejectedBeforeLoading,
    RejectedDelivery,
    PartialDelivery,
    AddressIssue,
    CustomerUnavailable,
    AccessDenied,
    VehicleIssue,
    Delay,
    Other,
}

public enum ExceptionSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

public enum ExecutionExceptionStatus
{
    Open,
    Investigating,
    Resolved,
    Rejected,
    CustomerActionRequired,
}

/// <summary>
/// A structured problem report from execution: anchored to a trip, optionally to a stop,
/// order and cargo item. Resolution runs a controlled workflow (terminal decisions require
/// a note); dispatcher notes and the customer-visible flag stay back-office concerns.
/// Latitude/longitude are the geolocation extension point — filled when the client has a fix.
/// </summary>
public class ExecutionException : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid? TransportOrderId { get; set; }
    public Guid? TransportOrderStopId { get; set; }
    public Guid? CargoItemId { get; set; }

    /// <summary>Resolved tracked package for package-level exceptions (missing/wrong/damaged/...).</summary>
    public Guid? PackageId { get; set; }

    public ExecutionExceptionType Type { get; set; }
    public ExceptionSeverity Severity { get; set; } = ExceptionSeverity.Medium;
    public ExecutionExceptionStatus Status { get; set; } = ExecutionExceptionStatus.Open;

    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }

    public Guid? ReportedByUserId { get; set; }
    public Guid? DriverId { get; set; }
    public DateTime OccurredAt { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string? DispatcherNotes { get; set; }

    /// <summary>Dispatcher currently owning the follow-up; null means unassigned.</summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Whether this exception may surface in customer-facing views (portal/notifications later).</summary>
    public bool CustomerVisible { get; set; }

    public string? ResolutionNote { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public List<ExceptionPhoto> Photos { get; set; } = [];
}

/// <summary>Photo evidence stored through the shared file-storage abstraction (opaque key).</summary>
public class ExceptionPhoto : AuditableTenantEntity
{
    public Guid ExecutionExceptionId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}
