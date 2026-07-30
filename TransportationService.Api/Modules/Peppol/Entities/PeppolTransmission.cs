using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Peppol.Entities;

/// <summary>
/// One outbound Peppol document lifecycle (invoice or credit note) sent through a provider.
/// At most one NON-TERMINAL transmission (status not in Failed/Rejected/Cancelled) may exist
/// per invoice — enforced by a filtered unique index (valid on both PostgreSQL and the SQLite
/// test harness); the queue service performs its own duplicate check first to surface a
/// friendly Dutch error instead of a database exception.
/// </summary>
public class PeppolTransmission : AuditableTenantEntity
{
    public Guid InvoiceId { get; set; }
    public PeppolDocumentKind DocumentKind { get; set; } = PeppolDocumentKind.Invoice;
    public PeppolTransmissionStatus Status { get; set; } = PeppolTransmissionStatus.Queued;
    public PeppolEnvironment Environment { get; set; } = PeppolEnvironment.Sandbox;
    public string ProviderKey { get; set; } = "sandbox";
    public string? ProviderMessageId { get; set; }

    /// <summary>Combined "scheme:id" Peppol participant identifiers of both parties.</summary>
    public string SellerParticipant { get; set; } = string.Empty;
    public string BuyerParticipant { get; set; } = string.Empty;

    /// <summary>Storage key of the rendered document payload (category "peppol" on IFileStorageService).</summary>
    public string? PayloadStorageKey { get; set; }

    /// <summary>SHA-256 hash (hex) of the payload, for integrity/idempotency checks.</summary>
    public string? PayloadHash { get; set; }

    public int PayloadVersion { get; set; } = 1;

    /// <summary>Sanitized failure detail — never provider credentials or tokens.</summary>
    public string? ErrorDetail { get; set; }
    public string? ResponseCode { get; set; }
    public int RetryCount { get; set; }

    /// <summary>Dispatcher backoff: Queued rows are only picked up once this moment has passed.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Stable id correlating this transmission's provider calls end-to-end.</summary>
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public List<PeppolTransmissionEvent> Events { get; set; } = [];
}

/// <summary>One immutable status transition on a <see cref="PeppolTransmission"/>. Append-only — no update/delete API.</summary>
public class PeppolTransmissionEvent : AuditableTenantEntity
{
    public Guid TransmissionId { get; set; }
    public PeppolTransmissionStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Detail { get; set; }
}
