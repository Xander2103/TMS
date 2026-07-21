using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Organization.Entities;

/// <summary>
/// An own company / invoicing entity of the tenant ("facturerende entiteit"). One tenant can
/// operate several legal entities (transport BV, logistics division, warehousing entity, ...).
/// Invoices are issued by exactly one legal entity; invoice numbering runs per entity per month
/// (see InvoiceSequence). The default entity is bootstrapped from TenantSettings company data.
/// Selecting an "active" entity in the UI is a user preference and never widens tenant access.
/// </summary>
public class LegalEntity : AuditableTenantEntity
{
    public string LegalName { get; set; } = string.Empty;
    public string? TradingName { get; set; }
    public string? CompanyNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? PeppolId { get; set; }
    public string? PeppolScheme { get; set; }

    // Registered address
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    // Contact
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }

    // Banking
    public string? Iban { get; set; }
    public string? Bic { get; set; }
    public string? BankName { get; set; }

    // Invoicing defaults
    public string DefaultCurrency { get; set; } = "EUR";
    public int PaymentTermDays { get; set; } = 30;

    /// <summary>
    /// Invoice number template. Tokens: {YYYY} {YY} {MM} {SEQ} {PREFIX}. The sequence is
    /// per entity per invoice month (year+month), zero-padded to <see cref="InvoiceSequencePadding"/>.
    /// </summary>
    public string InvoiceNumberFormat { get; set; } = "{YYYY}{MM}{SEQ}";

    /// <summary>Zero-padding width of the {SEQ} token (2-8).</summary>
    public int InvoiceSequencePadding { get; set; } = 4;

    /// <summary>Free text rendered by the {PREFIX} token (e.g. "FAC-").</summary>
    public string? InvoicePrefix { get; set; }

    /// <summary>Footer text printed on invoices of this entity.</summary>
    public string? InvoiceFooter { get; set; }

    /// <summary>Opaque IFileStorageService key of the uploaded logo (category "legal-entities").</summary>
    public string? LogoStorageKey { get; set; }
    public string? LogoFileName { get; set; }
    public string? LogoContentType { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Exactly one default entity per tenant (filtered unique index).</summary>
    public bool IsDefault { get; set; }
}
