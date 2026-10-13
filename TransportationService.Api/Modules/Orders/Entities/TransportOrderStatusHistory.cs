namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// Immutable, append-only status trail of a transport order. Rows are written by the
/// status-history interceptor for EVERY status mutation — manual transitions, cancel,
/// corrections, planning-driven changes and the invoicing flip — so the history can never
/// silently miss a path. Never updated or deleted.
/// </summary>
public class TransportOrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TransportOrderId { get; set; }

    public TransportOrderStatus FromStatus { get; set; }
    public TransportOrderStatus ToStatus { get; set; }

    /// <summary>Mandatory for cancellations and corrective (backward) transitions.</summary>
    public string? Reason { get; set; }

    /// <summary>True when this row was produced by the controlled correction flow.</summary>
    public bool IsCorrection { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}
