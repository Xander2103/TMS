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
    DateTime CreatedAt)
{
    /// <summary>Operational filters/columns (remediation): order, trip, driver and vehicle of the incident.</summary>
    public string? OrderNumber { get; init; }
    public string? TripNumber { get; init; }
    public string? DriverName { get; init; }
    public string? VehicleLabel { get; init; }
}

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
    bool RedeliverySuggested = false)
{
    /// <summary>Where the incident came from: Office | Driver | FailedDelivery | Exception.</summary>
    public string Source { get; init; } = "Office";
    public Guid? SourceExceptionId { get; init; }
    public string? SourceExceptionLabel { get; init; }

    /// <summary>Resolution actor (with <see cref="IncidentDetailDto.ResolvedAt"/> and Resolution).</summary>
    public Guid? ResolvedByUserId { get; init; }
    public string? ResolvedByName { get; init; }

    /// <summary>Customer-facing wording (the description stays internal).</summary>
    public string? CustomerMessage { get; init; }

    /// <summary>EX-03: the fleet damage report created from this incident, with its fleet state.</summary>
    public Guid? DamageReportId { get; init; }
    public string? DamageReportSeverity { get; init; }
    public string? DamageReportStatus { get; init; }

    /// <summary>EX-03: a vehicle/trailer is known, so a linked damage report can be created.</summary>
    public bool CanCreateDamageReport { get; init; }

    /// <summary>EX-03: vehicle-related and serious — the UI prompts "Schademelding aanmaken".</summary>
    public bool VehicleSafetyIssue { get; init; }

    /// <summary>Execution relations (trip/driver/trailer) are locked: set by the source, never by an office edit.</summary>
    public bool ExecutionLinksLocked { get; init; }

    /// <summary>EX-05: a dossier this incident concerns was already confirmed when it was registered.</summary>
    public IReadOnlyList<string> ConfirmedDossierNumbers { get; init; } = [];
}

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
    string? ResponsibilityNotes = null,
    string? CustomerMessage = null);

/// <summary>
/// EX-01: the office update model. Every section is OPTIONAL and applied only when present, so a
/// form that edits one card can never erase what another card (or the driver app, or the
/// failed-delivery automation) recorded. Trip/driver/trailer are execution relations: they can be
/// filled in when empty, never changed or cleared by an office edit.
/// </summary>
public record UpdateIncidentRequest(
    IncidentReportSection? Report = null,
    IncidentLinksSection? Links = null,
    IncidentImpactSection? Impact = null,
    IncidentResponsibilitySection? Responsibility = null,
    IncidentCommunicationSection? Communication = null);

public record IncidentReportSection(
    string Title,
    string Description,
    string IncidentType,
    string Severity,
    string? CustomTypeName = null,
    string? Cause = null,
    Guid? ResponsibleUserId = null,
    DateOnly? DueDate = null);

public record IncidentLinksSection(
    Guid? CustomerId = null,
    Guid? DossierId = null,
    Guid? TransportOrderId = null,
    Guid? VehicleId = null,
    Guid? TrailerId = null,
    Guid? TripId = null,
    Guid? DriverId = null);

public record IncidentImpactSection(
    string? CustomerImpact = null,
    string? OperationalImpact = null,
    string? FinancialImpact = null,
    decimal? EstimatedCost = null,
    decimal? ActualCost = null);

public record IncidentResponsibilitySection(string ResponsibleParty, string? ResponsibilityNotes = null);

public record IncidentCommunicationSection(string? CustomerMessage);

/// <summary>EX-07: a person who may be made responsible for an incident/afwijking — safe fields only.</summary>
public record AssignableUserDto(Guid Id, string DisplayName, string? RoleName);

/// <summary>EX-03: create the fleet damage report for a vehicle-related incident/afwijking.</summary>
public record CreateLinkedDamageReportRequest(string Severity, string? Description = null, string? Location = null);

/// <summary>Redelivery: what to re-deliver. Quantity null = the not-delivered quantity the system derives.</summary>
public record CreateRedeliveryRequest(decimal? Quantity = null, bool Free = false, string? FreeReason = null);

/// <summary>Domain history for operational roles: when / who / what — never the raw audit payload.</summary>
public record ProblemHistoryEntryDto(Guid Id, DateTime At, string? ActorName, string Action, string? Detail);

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
    string ChargeDecision = "None")
{
    /// <summary>Machine type code + its Dutch label (incident type, or afwijking type) — never a raw enum on screen.</summary>
    public string TypeCode { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public string? ResponsibleName { get; init; }
    public string? CustomerName { get; init; }
}

/// <summary>Wave 6 §2: propose charging this problem to the customer (incidents.manage).</summary>
public record ProposeIncidentChargeRequest(decimal Amount, string Description);

/// <summary>Wave 6 §2: approve/reject the proposed charge (problems.approve_charge).</summary>
public record DecideIncidentChargeRequest(bool Approve);

public record ChangeIncidentStatusRequest(string Status, string? Resolution = null);
