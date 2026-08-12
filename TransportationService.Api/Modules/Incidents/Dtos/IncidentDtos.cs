namespace TransportationService.Api.Modules.Incidents.Dtos;

public record IncidentListItemDto(
    Guid Id,
    string Title,
    string IncidentType,
    string? CustomTypeName,
    string Status,
    string Severity,
    string? CustomerName,
    string? ResponsibleName,
    string? DossierNumber,
    DateOnly? DueDate,
    bool IsOverdue,
    DateTime CreatedAt);

public record IncidentDetailDto(
    Guid Id,
    string Title,
    string Description,
    string IncidentType,
    string? CustomTypeName,
    string Status,
    string Severity,
    string? Cause,
    Guid? ResponsibleUserId,
    string? ResponsibleName,
    string? CustomerImpact,
    string? OperationalImpact,
    string? FinancialImpact,
    decimal? EstimatedCost,
    decimal? ActualCost,
    Guid? CustomerId,
    string? CustomerName,
    Guid? DriverId,
    string? DriverName,
    Guid? VehicleId,
    string? VehicleLabel,
    Guid? TrailerId,
    string? TrailerLabel,
    Guid? TransportOrderId,
    string? TransportOrderNumber,
    Guid? TripId,
    string? TripNumber,
    Guid? DossierId,
    string? DossierNumber,
    DateOnly? DueDate,
    string? Resolution,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    IReadOnlyList<string> AllowedStatusChanges,
    /// <summary>Wave 6 §1: Unknown | Customer | Own | Driver | Supplier.</summary>
    string ResponsibleParty = "Unknown",
    string? ResponsibilityNotes = null,
    /// <summary>Wave 6 §2: None | Proposed | Approved | Rejected.</summary>
    string ChargeDecision = "None",
    decimal? ChargeAmount = null,
    string? ChargeDescription = null,
    /// <summary>Wave 6 §3: the redelivery order created from this incident.</summary>
    Guid? LinkedRedeliveryOrderId = null,
    string? LinkedRedeliveryOrderNumber = null,
    /// <summary>P4 "Propose" mode: dispatch sees an explicit redelivery recommendation.</summary>
    bool RedeliverySuggested = false);

public record SaveIncidentRequest(
    string Title,
    string Description,
    string IncidentType,
    string Severity,
    string? CustomTypeName = null,
    string? Cause = null,
    Guid? ResponsibleUserId = null,
    string? CustomerImpact = null,
    string? OperationalImpact = null,
    string? FinancialImpact = null,
    decimal? EstimatedCost = null,
    decimal? ActualCost = null,
    Guid? CustomerId = null,
    Guid? DriverId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    Guid? TransportOrderId = null,
    Guid? TripId = null,
    Guid? DossierId = null,
    DateOnly? DueDate = null,
    /// <summary>Offline-replay idempotency key (driver app); a repeated key returns the stored incident.</summary>
    Guid? ClientRequestId = null,
    /// <summary>Wave 6 §1: Unknown | Customer | Own | Driver | Supplier.</summary>
    string ResponsibleParty = "Unknown",
    string? ResponsibilityNotes = null);

/// <summary>Wave 6 §4: one row of the unified problem list (incidents + execution exceptions).</summary>
public record ProblemListItemDto(
    Guid Id,
    /// <summary>"Incident" | "Exception" — drives the link target.</summary>
    string Kind,
    string Title,
    string Severity,
    string Status,
    DateTime OccurredAt,
    string? OrderNumber,
    string? TripNumber,
    Guid? TripId,
    string? DossierNumber,
    Guid? DossierId,
    string ResponsibleParty = "Unknown",
    string ChargeDecision = "None");

/// <summary>Wave 6 §2: propose charging this problem to the customer (incidents.manage).</summary>
public record ProposeIncidentChargeRequest(decimal Amount, string Description);

/// <summary>Wave 6 §2: approve/reject the proposed charge (problems.approve_charge).</summary>
public record DecideIncidentChargeRequest(bool Approve);

public record ChangeIncidentStatusRequest(string Status, string? Resolution = null);
