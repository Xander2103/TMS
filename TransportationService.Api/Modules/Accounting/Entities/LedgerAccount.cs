using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Accounting.Entities;

/// <summary>
/// Tenant-scoped general-ledger account master data (grootboekrekening). Every company
/// configures its own numbers — nothing is ever hardcoded ("Transport = 700000" does not
/// exist). Inactive accounts cannot be newly assigned but stay visible historically.
/// </summary>
public class LedgerAccount : AuditableTenantEntity
{
    public string AccountNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional code in the external accounting package.</summary>
    public string? ExternalCode { get; set; }

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// How the invoice generator categorises a line automatically. None = only manually
/// selectable (e.g. "Verkoop europallets"). At most one ACTIVE category per non-None role.
/// </summary>
public enum SalesCategorySystemRole
{
    None = 0,
    /// <summary>The base transport line of an order.</summary>
    Transport = 1,
    /// <summary>Service/supplement lines (from TransportOrderServiceLine).</summary>
    Surcharge = 2,
    /// <summary>Diesel-surcharge lines.</summary>
    Diesel = 3,
}

/// <summary>
/// A sales-line category ("Verkoopcategorie": Transport, Supplementen, Diesel, Verkoop
/// europallets, ...) with its tenant-specific mapping to ONE ledger account. The mapping is
/// LIVE configuration — invoice lines snapshot the resolved account at finalization, so
/// changing it later never rewrites history.
/// </summary>
public class SalesCategory : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SalesCategorySystemRole SystemRole { get; set; } = SalesCategorySystemRole.None;

    /// <summary>The configured ledger account; null = unmapped (draft warning + export blocker).</summary>
    public Guid? LedgerAccountId { get; set; }
    public LedgerAccount? LedgerAccount { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Invoice-line text for this sales code (Wave 2); null falls back to Name.</summary>
    public string? InvoiceDescriptionNl { get; set; }

    /// <summary>Default managed unit for manual lines with this code (Wave 2).</summary>
    public string? DefaultUnitCode { get; set; }

    /// <summary>
    /// UNCL5305 VAT category forced by this sales code (Wave 2); null = the customer's VAT
    /// treatment decides (VatTreatmentCatalog chain, the default and the norm).
    /// </summary>
    public string? VatCategoryOverride { get; set; }

    // --- Sprint 5: commercial article master ---------------------------------
    // The sales code IS this record; these fields complete it rather than introducing a
    // second article table. InvoiceDescriptionNl above is the Dutch member of the set below.

    /// <summary>Approved French invoice description; null falls back to Dutch, then to Name.</summary>
    public string? InvoiceDescriptionFr { get; set; }

    /// <inheritdoc cref="InvoiceDescriptionFr"/>
    public string? InvoiceDescriptionEn { get; set; }

    /// <inheritdoc cref="InvoiceDescriptionFr"/>
    public string? InvoiceDescriptionDe { get; set; }

    /// <summary>
    /// "Meetellen in basis dieseltoeslag". Transport counts; an administrative fee does not.
    /// The diesel code itself is excluded structurally by its <see cref="SystemRole"/>, so the
    /// surcharge can never be charged over itself.
    /// </summary>
    public bool IncludeInDieselBase { get; set; }

    /// <summary>
    /// EXCEPTIONAL statutory classification for this code. Null (the normal case) leaves the
    /// customer's fiscal treatment in charge; set it only for codes that are always treated
    /// differently regardless of the customer.
    /// </summary>
    public VatTreatment? VatTreatmentOverride { get; set; }

    /// <summary>Optional cost centre for the accounting export.</summary>
    public string? CostCentre { get; set; }

    /// <summary>Optional simple default price for codes with one fixed amount.</summary>
    public decimal? DefaultUnitPrice { get; set; }

    /// <summary>Free-form pricing-basis hint (e.g. "Hourly", "PerKm") for the pricing engine.</summary>
    public string? DefaultPricingBasis { get; set; }

    public string? Notes { get; set; }

    /// <summary>Per-invoicing-entity ledger overrides; empty = every entity uses <see cref="LedgerAccountId"/>.</summary>
    public List<SalesCategoryLedgerMapping> LedgerMappings { get; set; } = [];
}

/// <summary>
/// Per-invoicing-entity ledger mapping for the SAME sales code — entity A books ADM on
/// 700000, entity B on 704100. Exists so a code is never duplicated just because the
/// accounting export differs.
/// </summary>
public class SalesCategoryLedgerMapping : AuditableTenantEntity
{
    public Guid SalesCategoryId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid LedgerAccountId { get; set; }
    public string? CostCentre { get; set; }
}
