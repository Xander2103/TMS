using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// One piece of operational work inside a dossier. Deliberately thin: for transport-shaped
/// work (type.HasStops) the linked TransportOrder IS the execution record — stops, cargo,
/// pricing, scanning and POD live there and are referenced, never copied. A field that
/// exists on TransportOrder must not be added here. Standalone activities (crane on site,
/// plateau, storage) carry only the minimal shared scalars; their own execution models
/// arrive in later waves. No own status: transport activities derive it live from the order,
/// standalone activities are descriptive.
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
}
