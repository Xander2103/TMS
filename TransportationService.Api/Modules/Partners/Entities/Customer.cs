using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Peppol.Entities;

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

    /// <summary>Short/nickname used internally (e.g. "AB Log" for "AB Logistics Belgium NV").</summary>
    public string? Nickname { get; set; }

    public string? VatNumber { get; set; }

    /// <summary>Company (enterprise) number, e.g. Belgian KBO 0xxx.xxx.xxx.</summary>
    public string? CompanyNumber { get; set; }

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

    /// <summary>Default own company that invoices this customer (orders/invoices inherit it).</summary>
    public Guid? DefaultLegalEntityId { get; set; }

    // Commercial terms
    public string? InvoiceEmail { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public string? DefaultLanguageCode { get; set; }
    public string CurrencyCode { get; set; } = "EUR";

    // Banking (optional; for direct debits / refunds)
    public string? Iban { get; set; }
    public string? Bic { get; set; }
    public string? BankName { get; set; }
    /// <summary>Non-IBAN account reference for customers outside the SEPA zone.</summary>
    public string? BankAccountNumber { get; set; }

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

    /// <summary>Whether this customer is set up to receive invoices via Peppol (requires PeppolId+PeppolScheme).</summary>
    public bool PeppolEnabled { get; set; }

    /// <summary>Outcome of the last provider participant lookup for this customer's Peppol id.</summary>
    public CustomerPeppolValidationStatus PeppolValidationStatus { get; set; } = CustomerPeppolValidationStatus.Unknown;
    public DateTime? PeppolValidatedAt { get; set; }

    /// <summary>Provider reference returned by the last successful validation (opaque, no secrets).</summary>
    public string? PeppolValidationReference { get; set; }

    /// <summary>Buyer reference to include on outgoing Peppol invoices for this customer (e.g. a cost-centre code).</summary>
    public string? BuyerReference { get; set; }

    /// <summary>How this customer should receive invoices when Peppol is enabled.</summary>
    public PeppolDeliveryPreference PeppolDeliveryPreference { get; set; } = PeppolDeliveryPreference.Peppol;

    /// <summary>Invoice language override; null = DefaultLanguageCode.</summary>
    public string? InvoiceLanguageCode { get; set; }

    // Order-intake requirements for this customer.
    /// <summary>
    /// PO-number policy (None/Optional/Required). The legacy PurchaseOrderRequired bool is
    /// kept in sync on write for backward compatibility but the policy is authoritative.
    /// </summary>
    public PurchaseOrderPolicy PurchaseOrderPolicy { get; set; } = PurchaseOrderPolicy.None;

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
