using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Accounting.Services;

/// <summary>Where a line's fiscal treatment came from — shown on the invoice preview.</summary>
public enum FiscalTreatmentSource
{
    /// <summary>An authorised, explicit override on this invoice line.</summary>
    LineOverride,

    /// <summary>The sales code carries an exceptional statutory classification.</summary>
    SalesCode,

    /// <summary>The customer's configured fiscal treatment — the normal case.</summary>
    Customer,

    /// <summary>Nothing configured anywhere; the tenant/legal-entity default applied.</summary>
    TenantDefault,
}

/// <param name="Treatment">The treatment that applies to this line.</param>
/// <param name="RatePercent">The VAT percentage to invoice.</param>
/// <param name="Source">Which level in the hierarchy decided it.</param>
/// <param name="VatCategoryCode">UNCL5305 category for UBL/Peppol.</param>
/// <param name="LegalText">Statutory wording for the invoice, when the treatment has one.</param>
public record FiscalResolution(
    VatTreatment Treatment,
    decimal RatePercent,
    FiscalTreatmentSource Source,
    string VatCategoryCode,
    string? LegalText);

/// <summary>One thing that looks wrong about a fiscal setup; never changes the outcome.</summary>
public record FiscalWarning(string Code, string Message);

/// <summary>
/// Sprint 5C — the single place that decides the fiscal treatment of an invoice line.
///
/// The sales code here is the existing <see cref="SalesCategory"/> record (the thing order and
/// invoice lines already snapshot); this wave completed it into the commercial article master
/// rather than introducing a second one.
///
/// The hierarchy is deliberate and fixed:
/// <list type="number">
/// <item>an explicit, authorised line override;</item>
/// <item>a sales code with an EXCEPTIONAL statutory classification;</item>
/// <item>the CUSTOMER's configured fiscal treatment — the normal path;</item>
/// <item>the tenant/legal-entity default.</item>
/// </list>
///
/// Country and VAT number are used to WARN about a suspicious configuration, never to decide.
/// There is no "foreign customer ⇒ 0%" rule anywhere: a customer invoiced with domestic VAT
/// stays on domestic VAT until someone changes the customer's configuration.
/// </summary>
public static class InvoiceLineFiscalResolver
{
    /// <param name="lineOverride">Set only when an authorised user overrode this line.</param>
    /// <param name="salesCode">The line's sales code, when it has one.</param>
    /// <param name="customerTreatment">The customer's configured treatment.</param>
    /// <param name="customerRatePercent">The customer's own default rate, when configured.</param>
    /// <param name="tenantDefaultRatePercent">Fallback rate for domestic VAT.</param>
    public static FiscalResolution Resolve(
        VatTreatment? lineOverride,
        SalesCategory? salesCode,
        VatTreatment? customerTreatment,
        decimal? customerRatePercent,
        decimal tenantDefaultRatePercent)
    {
        var (treatment, source) =
            lineOverride is { } overridden ? (overridden, FiscalTreatmentSource.LineOverride)
            : salesCode?.VatTreatmentOverride is { } coded ? (coded, FiscalTreatmentSource.SalesCode)
            : customerTreatment is { } customer ? (customer, FiscalTreatmentSource.Customer)
            : (VatTreatment.DomesticVat, FiscalTreatmentSource.TenantDefault);

        var info = VatTreatmentCatalog.Resolve(treatment);

        // A treatment with a fixed statutory rate (0% for reverse charge, exempt, …) wins over
        // any stored customer rate: that rate only expresses WHICH domestic rate applies.
        var rate = info.DefaultRatePercent ?? customerRatePercent ?? tenantDefaultRatePercent;

        // The code may still force the UBL category (the pre-existing Wave 2 behaviour).
        var category = string.IsNullOrWhiteSpace(salesCode?.VatCategoryOverride)
            ? VatTreatmentCatalog.ResolveVatCategory(treatment, rate).Code
            : salesCode!.VatCategoryOverride!;

        return new FiscalResolution(treatment, rate, source, category, info.InvoiceLegalText);
    }

    /// <summary>
    /// Configuration that looks inconsistent. These are shown to the user for review; they
    /// never silently rewrite the configured treatment (rule 3 of the wave).
    /// </summary>
    public static IReadOnlyList<FiscalWarning> Inspect(
        VatTreatment customerTreatment,
        string? customerVatNumber,
        string? customerCountryCode,
        string? tenantCountryCode)
    {
        var warnings = new List<FiscalWarning>();
        var info = VatTreatmentCatalog.Resolve(customerTreatment);

        if (info.RequiresVatNumber && string.IsNullOrWhiteSpace(customerVatNumber))
        {
            warnings.Add(new FiscalWarning(
                "vat-number-missing",
                $"'{info.Label}' vereist een btw-nummer bij deze klant; zonder nummer kan de factuur niet verzonden worden."));
        }

        var customerCountry = customerCountryCode?.Trim().ToUpperInvariant();
        var tenantCountry = tenantCountryCode?.Trim().ToUpperInvariant();

        // A foreign customer on domestic VAT is not wrong — cross-border road transport often
        // is domestic-taxed — but it is worth a look.
        if (customerTreatment == VatTreatment.DomesticVat
            && customerCountry is { Length: 2 } && tenantCountry is { Length: 2 }
            && customerCountry != tenantCountry)
        {
            warnings.Add(new FiscalWarning(
                "domestic-vat-foreign-customer",
                $"Deze klant staat in {customerCountry} maar wordt met binnenlandse btw gefactureerd. Controleer of dat klopt."));
        }

        if (customerTreatment == VatTreatment.IntraCommunitySupply
            && customerCountry is { Length: 2 } && tenantCountry is { Length: 2 }
            && customerCountry == tenantCountry)
        {
            warnings.Add(new FiscalWarning(
                "intra-community-same-country",
                "Intracommunautaire levering met een klant in hetzelfde land als de facturerende entiteit."));
        }

        return warnings;
    }

    /// <summary>
    /// The customer-facing description of a line: the APPROVED description for the invoice
    /// language, falling back to Dutch and then to the internal name. Never translated on the
    /// fly — a finalized invoice must stay reproducible.
    /// </summary>
    public static string DescriptionFor(SalesCategory salesCode, string? invoiceLanguageCode)
    {
        var description = invoiceLanguageCode?.Trim().ToLowerInvariant() switch
        {
            "fr" => salesCode.InvoiceDescriptionFr,
            "en" => salesCode.InvoiceDescriptionEn,
            "de" => salesCode.InvoiceDescriptionDe,
            "nl" => salesCode.InvoiceDescriptionNl,
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(description)) return description!;
        return !string.IsNullOrWhiteSpace(salesCode.InvoiceDescriptionNl)
            ? salesCode.InvoiceDescriptionNl!
            : salesCode.Name;
    }

    /// <summary>
    /// Ledger account + cost centre for a sales code under one invoicing entity: the
    /// entity-specific mapping when there is one, else the code's own default.
    /// </summary>
    public static (Guid? LedgerAccountId, string? CostCentre) LedgerFor(SalesCategory salesCode, Guid? legalEntityId)
    {
        if (legalEntityId is { } entityId)
        {
            var mapping = salesCode.LedgerMappings.FirstOrDefault(m => m.LegalEntityId == entityId);
            if (mapping is not null) return (mapping.LedgerAccountId, mapping.CostCentre ?? salesCode.CostCentre);
        }

        return (salesCode.LedgerAccountId, salesCode.CostCentre);
    }

    /// <summary>
    /// The diesel-surcharge base: only amounts whose sales code is explicitly flagged as
    /// counting. A code whose system role IS the diesel surcharge is excluded structurally, so
    /// diesel can never be charged over diesel even if the flag is ticked by mistake.
    /// </summary>
    public static decimal DieselBase(IEnumerable<(SalesCategory? Code, decimal Amount)> lines) =>
        lines
            .Where(l => l.Code is { IncludeInDieselBase: true }
                        && l.Code.SystemRole != SalesCategorySystemRole.Diesel)
            .Sum(l => l.Amount);
}
