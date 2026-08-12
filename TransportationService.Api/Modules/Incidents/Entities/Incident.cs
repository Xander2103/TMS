using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Incidents.Entities;

public enum IncidentType
{
    Damage,
    Delay,
    Theft,
    Accident,
    WrongDelivery,
    MissingGoods,
    CustomerComplaint,
    VehicleBreakdown,
    Administrative,
    Other,
}

public enum IncidentStatus
{
    New,
    InProgress,
    Resolved,
    Cancelled,
}

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// An operational incident (damage, delay, complaint, ...). Every link is optional so an
/// incident can be registered the moment it is reported and enriched later; resolving one
/// requires an explicit resolution text. Costs are registered here and roll up into the
/// dossier's financial summary when the incident is attached to a dossier.
/// </summary>
public class Incident : AuditableTenantEntity
{
    public IncidentType IncidentType { get; set; }
    /// <summary>Free-text type name, required when <see cref="IncidentType.Other"/>.</summary>
    public string? CustomTypeName { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Cause { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.New;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;

    public Guid? ResponsibleUserId { get; set; }

    // Impact assessment (free text per dimension)
    public string? CustomerImpact { get; set; }
    public string? OperationalImpact { get; set; }
    public string? FinancialImpact { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }

    // Optional links to the records the incident is about
    public Guid? CustomerId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }
    public Guid? TransportOrderId { get; set; }
    public Guid? TripId { get; set; }
    public Guid? DossierId { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Resolution { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Client idempotency key of the create request (driver-app offline replay).</summary>
    public Guid? ClientRequestId { get; set; }

    // --- Wave 6 §1: responsibility attribution (additive) ---

    /// <summary>Unknown | Customer | Own | Driver | Supplier — who caused/bears the problem.</summary>
    public string ResponsibleParty { get; set; } = "Unknown";
    public string? ResponsibilityNotes { get; set; }

    // --- Wave 6 §2: approval-gated charge decision ---

    /// <summary>None | Proposed | Approved | Rejected. Approving needs problems.approve_charge
    /// (v28) and creates a Manual pricing line on the linked order when possible.</summary>
    public string ChargeDecision { get; set; } = "None";
    public decimal? ChargeAmount { get; set; }
    public string? ChargeDescription { get; set; }
    public Guid? ChargeDecidedByUserId { get; set; }
    public DateTime? ChargeDecidedAt { get; set; }

    // --- Wave 6 §3: linked redelivery ---

    public Guid? LinkedRedeliveryOrderId { get; set; }
}
