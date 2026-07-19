using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Edi.Entities;

public enum EdiDirection
{
    Inbound,
    Outbound,
}

public enum EdiProcessingStatus
{
    Received,
    Processed,
    Failed,
    DeadLettered,
    Duplicate,
}

/// <summary>
/// One EDI trading partner. The mapping profile names the parser implementation; no
/// partner-specific wire format exists until a real specification/sample arrives.
/// </summary>
public class TradingPartner : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Our customer this partner orders for.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>The partner's identifier for that customer relation (their side).</summary>
    public string? ExternalCustomerIdentifier { get; set; }

    /// <summary>Parser profile, e.g. "generic-json-v1".</summary>
    public string MappingProfile { get; set; } = "generic-json-v1";

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>Maps a partner's location code onto one of our master locations.</summary>
public class EdiPartnerLocation : AuditableTenantEntity
{
    public Guid TradingPartnerId { get; set; }
    public string ExternalLocationCode { get; set; } = string.Empty;
    public Guid LocationId { get; set; }
}

/// <summary>
/// One inbound or outbound EDI message with its payload, processing state and result link.
/// Failures keep full error detail; three failed attempts dead-letter; manual replay always
/// stays possible. Duplicates are stored (audit) but never reprocessed.
/// </summary>
public class EdiMessage : AuditableTenantEntity
{
    public EdiDirection Direction { get; set; }
    public Guid TradingPartnerId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    /// <summary>The partner's own reference (external order id / message id).</summary>
    public string? ExternalReference { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;

    public EdiProcessingStatus Status { get; set; } = EdiProcessingStatus.Received;
    public int AttemptCount { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorDetail { get; set; }
    public string? ValidationErrorsJson { get; set; }

    public string? ResultEntityType { get; set; }
    public string? ResultEntityId { get; set; }
}
