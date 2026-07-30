namespace TransportationService.Api.Modules.Peppol.Entities;

/// <summary>Which Peppol network a settings row / transmission targets.</summary>
public enum PeppolEnvironment
{
    Sandbox = 0,
    Live = 1,
}

/// <summary>Outbound document kind carried by a <see cref="PeppolTransmission"/>.</summary>
public enum PeppolDocumentKind
{
    Invoice = 0,
    CreditNote = 1,
}

/// <summary>
/// Lifecycle of one outbound Peppol document. Draft/Validated/Queued are local states before
/// the provider is contacted; SubmittedToProvider through Delivered/Rejected mirror provider
/// acknowledgements; Failed is a local/transport failure; Cancelled is a local withdrawal
/// before submission.
/// </summary>
public enum PeppolTransmissionStatus
{
    /// <summary>Reserved; transmissions are created directly as Queued today.</summary>
    Draft = 0,

    /// <summary>Reserved; transmissions are created directly as Queued today.</summary>
    Validated = 1,
    Queued = 2,
    SubmittedToProvider = 3,
    AcceptedByProvider = 4,
    Delivered = 5,
    Failed = 6,
    Rejected = 7,
    Cancelled = 8,
}

/// <summary>Terminal-state helpers shared by the "one active transmission per invoice" rule.</summary>
public static class PeppolTransmissionStatusExtensions
{
    private static readonly HashSet<PeppolTransmissionStatus> TerminalStatuses =
    [
        PeppolTransmissionStatus.Failed,
        PeppolTransmissionStatus.Rejected,
        PeppolTransmissionStatus.Cancelled,
    ];

    /// <summary>True for Failed/Rejected/Cancelled — the invoice is free to get a new transmission.</summary>
    public static bool IsTerminal(this PeppolTransmissionStatus status) => TerminalStatuses.Contains(status);
}

/// <summary>Kind of an inbound document landed in the review queue.</summary>
public enum PeppolIncomingDocumentKind
{
    SupplierInvoice = 0,
    SupplierCreditNote = 1,
    StatusMessage = 2,
}

/// <summary>Review-queue status of an inbound Peppol document.</summary>
public enum PeppolIncomingDocumentStatus
{
    Received = 0,
    NeedsReview = 1,
    Linked = 2,
    Rejected = 3,
}

/// <summary>Outcome of the last provider participant lookup for a customer.</summary>
public enum CustomerPeppolValidationStatus
{
    Unknown = 0,
    Found = 1,
    NotFound = 2,
}

/// <summary>How an enabled customer should receive invoices.</summary>
public enum PeppolDeliveryPreference
{
    Peppol = 0,
    EmailFallback = 1,
}
