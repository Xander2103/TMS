using TransportationService.Api.Common.Abstractions;

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
}
