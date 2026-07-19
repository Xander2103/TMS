using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Pod.Entities;

public enum PodOutcome
{
    Complete,
    Partial,
    Refused,
}

public enum PodPhotoCategory
{
    Delivery,
    Package,
    Document,
}

/// <summary>
/// One finalised, immutable proof-of-delivery version. Corrections never mutate a row:
/// they insert the next version (with mandatory reason and a link to the corrected one)
/// and move the IsCurrent flag, so every historical version stays byte-for-byte intact.
/// The scan summary is frozen as JSON at finalisation time — evidence, not a live view.
/// </summary>
public class ProofOfDelivery : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderId { get; set; }
    public Guid TransportOrderStopId { get; set; }

    public int Version { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;

    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientRole { get; set; }

    public PodOutcome Outcome { get; set; } = PodOutcome.Complete;
    public bool DamageReported { get; set; }
    public bool MissingReported { get; set; }
    public string? Notes { get; set; }

    public DateTime DeliveredAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Opaque storage key of the signature image; null when no signature was captured.</summary>
    public string? SignaturePath { get; set; }

    /// <summary>Frozen scan summary (JSON array) captured at finalisation.</summary>
    public string ScannedSummaryJson { get; set; } = "[]";

    public Guid? FinalisedByUserId { get; set; }
    public Guid? DriverId { get; set; }

    /// <summary>Why this version replaced the previous one (mandatory on corrections).</summary>
    public string? CorrectionReason { get; set; }
    public Guid? CorrectedFromPodId { get; set; }

    /// <summary>Whether this proof may surface in customer-facing views.</summary>
    public bool CustomerVisible { get; set; } = true;

    public List<PodPhoto> Photos { get; set; } = [];
}

/// <summary>Additive photo evidence on the current version; never deletable once attached.</summary>
public class PodPhoto : AuditableTenantEntity
{
    public Guid ProofOfDeliveryId { get; set; }

    public PodPhotoCategory Category { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}
