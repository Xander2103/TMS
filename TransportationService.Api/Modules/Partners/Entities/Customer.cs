using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>
/// A customer (shipper/consignor) of the transport company. Master record used across
/// quoting, order intake and invoicing.
/// </summary>
public class Customer : AuditableTenantEntity
{
    public string CustomerNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }

    public Guid? CategoryId { get; set; }

    // Primary contact details for the organisation
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }

    // Registered / visiting address
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    // Commercial terms
    public string? InvoiceEmail { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public string? DefaultLanguageCode { get; set; }

    // VAT profile. Treatment (how VAT is applied) is deliberately separate from the default
    // rate (which percentage), and the rate is nullable: null = use the tenant default.
    public VatTreatment VatTreatment { get; set; } = VatTreatment.DomesticVat;
    public decimal? DefaultVatRatePercent { get; set; }
    /// <summary>Country whose VAT regime applies; may differ from the visiting address.</summary>
    public string? VatCountryCode { get; set; }
    public string? VatNotes { get; set; }

    // Peppol e-invoicing identity (no external integration yet — data only).
    /// <summary>Peppol participant identifier without scheme prefix (e.g. "0123456789").</summary>
    public string? PeppolId { get; set; }
    /// <summary>Peppol scheme code (e.g. "0208" = Belgian enterprise number).</summary>
    public string? PeppolScheme { get; set; }

    /// <summary>Invoice language override; null = DefaultLanguageCode.</summary>
    public string? InvoiceLanguageCode { get; set; }

    // Order-intake requirements for this customer.
    public bool PurchaseOrderRequired { get; set; }
    public bool SignedDeliveryNoteRequired { get; set; }
    public bool CustomerReferenceRequired { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Credit/operational block preventing new orders. Independent of active/inactive.</summary>
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    public List<CustomerContact> Contacts { get; set; } = [];
}
