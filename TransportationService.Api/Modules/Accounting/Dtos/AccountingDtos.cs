using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Accounting.Dtos;

public record LedgerAccountDto(
    Guid Id, string AccountNumber, string Name, string? ExternalCode, string? Description, bool IsActive);

public record SaveLedgerAccountRequest(
    string AccountNumber, string Name, string? ExternalCode = null, string? Description = null, bool IsActive = true);

public record SalesCategoryDto(
    Guid Id, string Code, string Name, SalesCategorySystemRole SystemRole,
    Guid? LedgerAccountId, string? LedgerAccountNumber, string? LedgerAccountName,
    bool IsActive, int SortOrder,
    /// <summary>Wave 2: invoice-line text (falls back to Name), default unit for manual lines, forced UNCL5305 VAT category.</summary>
    string? InvoiceDescriptionNl = null, string? DefaultUnitCode = null, string? VatCategoryOverride = null,
    // --- Sprint 5: the commercial article master ---
    /// <summary>Approved customer-facing descriptions; the invoice uses the customer's invoice language.</summary>
    string? InvoiceDescriptionFr = null, string? InvoiceDescriptionEn = null, string? InvoiceDescriptionDe = null,
    /// <summary>"Meetellen in basis dieseltoeslag".</summary>
    bool IncludeInDieselBase = false,
    /// <summary>Exceptional statutory classification; null = the customer's treatment decides.</summary>
    VatTreatment? VatTreatmentOverride = null,
    string? CostCentre = null, decimal? DefaultUnitPrice = null, string? DefaultPricingBasis = null,
    string? Notes = null,
    /// <summary>Per-invoicing-entity ledger overrides.</summary>
    IReadOnlyList<SalesCategoryLedgerMappingDto>? LedgerMappings = null);

/// <summary>One entity-specific ledger override for a sales code (sprint 5G).</summary>
public record SalesCategoryLedgerMappingDto(
    Guid LegalEntityId, Guid LedgerAccountId, string? CostCentre);

public record SaveSalesCategoryRequest(
    string Code, string Name, SalesCategorySystemRole SystemRole = SalesCategorySystemRole.None,
    Guid? LedgerAccountId = null, bool IsActive = true, int SortOrder = 0,
    string? InvoiceDescriptionNl = null, string? DefaultUnitCode = null, string? VatCategoryOverride = null,
    string? InvoiceDescriptionFr = null, string? InvoiceDescriptionEn = null, string? InvoiceDescriptionDe = null,
    bool IncludeInDieselBase = false,
    VatTreatment? VatTreatmentOverride = null,
    string? CostCentre = null, decimal? DefaultUnitPrice = null, string? DefaultPricingBasis = null,
    string? Notes = null,
    IReadOnlyList<SalesCategoryLedgerMappingDto>? LedgerMappings = null);

/// <summary>Configuration health: which active sales categories still miss a ledger account.</summary>
public record AccountingHealthDto(IReadOnlyList<SalesCategoryDto> UnmappedCategories);
