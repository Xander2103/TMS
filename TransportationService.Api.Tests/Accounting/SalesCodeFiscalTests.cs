using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Tests.Accounting;

/// <summary>
/// Sprint 5 acceptance — the fiscal treatment of an invoice line, the persisted multilingual
/// descriptions, the diesel base and the per-entity ledger mapping.
///
/// The hierarchy under test is deliberate: line override → sales-code classification →
/// CUSTOMER treatment → tenant default. Country/VAT data only ever produces warnings.
/// </summary>
public class SalesCodeFiscalTests
{
    private const decimal TenantDefaultRate = 21m;

    private static SalesCategory Code(
        string code = "ADM", string name = "Administratieve kost",
        VatTreatment? vatOverride = null, bool dieselBase = false, bool isDiesel = false,
        string? nl = null, string? fr = null, string? en = null, string? de = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            VatTreatmentOverride = vatOverride,
            IncludeInDieselBase = dieselBase,
            SystemRole = isDiesel ? SalesCategorySystemRole.Diesel : SalesCategorySystemRole.None,
            InvoiceDescriptionNl = nl,
            InvoiceDescriptionFr = fr,
            InvoiceDescriptionEn = en,
            InvoiceDescriptionDe = de,
        };

    private static SalesCategory AdmWithTranslations() => Code(
        nl: "Administratieve kost",
        fr: "Frais administratifs",
        en: "Administrative fee",
        de: "Verwaltungsgebühr");

    // ------------------------------------------------------- scenarios A & B

    [Fact]
    public void A_DutchBelgianCustomer_GetsTheDutchDescriptionAndTheCustomersOwnTreatment()
    {
        var adm = AdmWithTranslations();

        var resolution = InvoiceLineFiscalResolver.Resolve(
            lineOverride: null, salesCode: adm,
            customerTreatment: VatTreatment.DomesticVat, customerRatePercent: 21m,
            tenantDefaultRatePercent: TenantDefaultRate);

        Assert.Equal(VatTreatment.DomesticVat, resolution.Treatment);
        Assert.Equal(FiscalTreatmentSource.Customer, resolution.Source);
        Assert.Equal(21m, resolution.RatePercent);
        Assert.Equal("S", resolution.VatCategoryCode);
        Assert.Equal("Administratieve kost", InvoiceLineFiscalResolver.DescriptionFor(adm, "nl"));
    }

    [Fact]
    public void B_FrenchLanguageCustomer_GetsTheFrenchDescriptionOfTheSameSalesCode()
    {
        var adm = AdmWithTranslations();

        Assert.Equal("Frais administratifs", InvoiceLineFiscalResolver.DescriptionFor(adm, "fr"));
        Assert.Equal("Administrative fee", InvoiceLineFiscalResolver.DescriptionFor(adm, "en"));
        Assert.Equal("Verwaltungsgebühr", InvoiceLineFiscalResolver.DescriptionFor(adm, "de"));
    }

    [Fact]
    public void MissingTranslation_FallsBackToDutchThenToTheInternalName()
    {
        var partial = Code(nl: "Administratieve kost");
        Assert.Equal("Administratieve kost", InvoiceLineFiscalResolver.DescriptionFor(partial, "fr"));

        var none = Code(name: "Interne naam");
        Assert.Equal("Interne naam", InvoiceLineFiscalResolver.DescriptionFor(none, "fr"));
    }

    // ----------------------------------------------------------- scenario C

    [Theory]
    [InlineData("TRANSPORT")]
    [InlineData("ADM")]
    [InlineData("WACHT")]
    public void C_NormalLines_InheritTheCustomersReverseChargeTreatment(string code)
    {
        var salesCode = Code(code: code, name: code);

        var resolution = InvoiceLineFiscalResolver.Resolve(
            lineOverride: null, salesCode: salesCode,
            customerTreatment: VatTreatment.ReverseCharge, customerRatePercent: 21m,
            tenantDefaultRatePercent: TenantDefaultRate);

        Assert.Equal(VatTreatment.ReverseCharge, resolution.Treatment);
        Assert.Equal(FiscalTreatmentSource.Customer, resolution.Source);
        // The statutory 0% of the treatment wins over the customer's stored 21%.
        Assert.Equal(0m, resolution.RatePercent);
        Assert.Equal("AE", resolution.VatCategoryCode);
        Assert.NotNull(resolution.LegalText);
    }

    // ----------------------------------------------------------- scenario D

    [Fact]
    public void D_ASalesCodeWithAStatutoryClassification_DeviatesOnThatLineOnly()
    {
        var exempt = Code(code: "DOORREK", name: "Doorrekening", vatOverride: VatTreatment.VatExempt);
        var normal = Code(code: "TRANSPORT", name: "Transport");

        var exemptLine = InvoiceLineFiscalResolver.Resolve(null, exempt, VatTreatment.DomesticVat, 21m, TenantDefaultRate);
        var normalLine = InvoiceLineFiscalResolver.Resolve(null, normal, VatTreatment.DomesticVat, 21m, TenantDefaultRate);

        Assert.Equal(VatTreatment.VatExempt, exemptLine.Treatment);
        Assert.Equal(FiscalTreatmentSource.SalesCode, exemptLine.Source);
        Assert.Equal(0m, exemptLine.RatePercent);

        // The rest of the invoice is untouched.
        Assert.Equal(VatTreatment.DomesticVat, normalLine.Treatment);
        Assert.Equal(21m, normalLine.RatePercent);
    }

    [Fact]
    public void AnAuthorisedLineOverride_OutranksEverythingAndIsRecordedAsSuch()
    {
        var code = Code(vatOverride: VatTreatment.VatExempt);

        var resolution = InvoiceLineFiscalResolver.Resolve(
            lineOverride: VatTreatment.DomesticVat, salesCode: code,
            customerTreatment: VatTreatment.ReverseCharge, customerRatePercent: 6m,
            tenantDefaultRatePercent: TenantDefaultRate);

        Assert.Equal(VatTreatment.DomesticVat, resolution.Treatment);
        Assert.Equal(FiscalTreatmentSource.LineOverride, resolution.Source);
        Assert.Equal(6m, resolution.RatePercent);
    }

    [Fact]
    public void WithoutAnyConfiguration_TheTenantDefaultApplies_NotAGuess()
    {
        var resolution = InvoiceLineFiscalResolver.Resolve(null, null, null, null, TenantDefaultRate);

        Assert.Equal(VatTreatment.DomesticVat, resolution.Treatment);
        Assert.Equal(FiscalTreatmentSource.TenantDefault, resolution.Source);
        Assert.Equal(21m, resolution.RatePercent);
    }

    // ------------------------------------------------------- scenarios E & F

    [Fact]
    public void E_DieselBase_CountsOnlyTheCodesThatAreFlaggedForIt()
    {
        var transport = Code(code: "TRANSPORT", name: "Transport", dieselBase: true);
        var adm = Code(code: "ADM", name: "Administratieve kost", dieselBase: false);

        var basis = InvoiceLineFiscalResolver.DieselBase([(transport, 500m), (adm, 25m)]);

        Assert.Equal(500m, basis);
        Assert.Equal(50m, Math.Round(basis * 10m / 100m, 2));
    }

    [Fact]
    public void F_TheDieselLineItself_IsNeverPartOfItsOwnBase()
    {
        var transport = Code(code: "TRANSPORT", name: "Transport", dieselBase: true);
        // Even with the box ticked by mistake, a surcharge code cannot inflate its own base.
        var diesel = Code(code: "DIESEL", name: "Dieseltoeslag", dieselBase: true, isDiesel: true);

        var basis = InvoiceLineFiscalResolver.DieselBase([(transport, 500m), (diesel, 50m)]);

        Assert.Equal(500m, basis);
    }

    [Fact]
    public void LinesWithoutASalesCode_DoNotSilentlyEnterTheDieselBase()
    {
        var transport = Code(code: "TRANSPORT", dieselBase: true);
        var basis = InvoiceLineFiscalResolver.DieselBase([(transport, 500m), (null, 999m)]);
        Assert.Equal(500m, basis);
    }

    // ----------------------------------------------------------- scenario G

    [Fact]
    public void G_TheSameSalesCode_BooksToADifferentLedgerPerInvoicingEntity()
    {
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var ledgerDefault = Guid.NewGuid();
        var ledgerA = Guid.NewGuid();
        var ledgerB = Guid.NewGuid();

        var adm = Code();
        adm.LedgerAccountId = ledgerDefault;
        adm.CostCentre = "ALG";
        adm.LedgerMappings.Add(new SalesCategoryLedgerMapping
        { Id = Guid.NewGuid(), SalesCategoryId = adm.Id, LegalEntityId = entityA, LedgerAccountId = ledgerA });
        adm.LedgerMappings.Add(new SalesCategoryLedgerMapping
        { Id = Guid.NewGuid(), SalesCategoryId = adm.Id, LegalEntityId = entityB, LedgerAccountId = ledgerB, CostCentre = "B-CC" });

        Assert.Equal((ledgerA, "ALG"), InvoiceLineFiscalResolver.LedgerFor(adm, entityA));
        Assert.Equal((ledgerB, "B-CC"), InvoiceLineFiscalResolver.LedgerFor(adm, entityB));
        // An entity without its own mapping falls back to the code's default — never to another entity's.
        Assert.Equal(((Guid?)ledgerDefault, "ALG"), InvoiceLineFiscalResolver.LedgerFor(adm, Guid.NewGuid()));
        Assert.Equal(((Guid?)ledgerDefault, "ALG"), InvoiceLineFiscalResolver.LedgerFor(adm, null));
    }

    // -------------------------------------------------------------- warnings

    [Fact]
    public void ReverseChargeWithoutAVatNumber_Warns_ButDoesNotChangeTheTreatment()
    {
        var warnings = InvoiceLineFiscalResolver.Inspect(
            VatTreatment.ReverseCharge, customerVatNumber: null, customerCountryCode: "NL", tenantCountryCode: "BE");

        Assert.Contains(warnings, w => w.Code == "vat-number-missing");

        // The configured treatment still stands: warnings never rewrite it.
        var resolution = InvoiceLineFiscalResolver.Resolve(null, null, VatTreatment.ReverseCharge, null, TenantDefaultRate);
        Assert.Equal(VatTreatment.ReverseCharge, resolution.Treatment);
    }

    [Fact]
    public void AForeignCustomerOnDomesticVat_IsFlaggedForReview_NotSilentlyZeroRated()
    {
        var warnings = InvoiceLineFiscalResolver.Inspect(
            VatTreatment.DomesticVat, customerVatNumber: "NL123456789B01",
            customerCountryCode: "NL", tenantCountryCode: "BE");

        Assert.Contains(warnings, w => w.Code == "domestic-vat-foreign-customer");

        // Crucially: the resolver still invoices domestic VAT at the normal rate.
        var resolution = InvoiceLineFiscalResolver.Resolve(null, null, VatTreatment.DomesticVat, null, TenantDefaultRate);
        Assert.Equal(VatTreatment.DomesticVat, resolution.Treatment);
        Assert.Equal(21m, resolution.RatePercent);
    }

    [Fact]
    public void ADomesticCustomerInTheSameCountry_ProducesNoWarnings()
    {
        Assert.Empty(InvoiceLineFiscalResolver.Inspect(VatTreatment.DomesticVat, "BE0123456749", "BE", "BE"));
    }

    // ---------------------------------------------- audit: one fiscal truth per line

    /// <summary>
    /// A legacy Wave 2 exemption category on a sales code ("AE" configured before the statutory
    /// classification field existed) can never yield "AE at 21%": it IS the classification, so
    /// treatment, rate, category and legal text agree — one fiscal truth per line.
    /// </summary>
    [Fact]
    public void ALegacyExemptionCategory_IsTheStatutoryClassification_NotAContradiction()
    {
        var code = Code();
        code.VatCategoryOverride = "AE";

        var resolution = InvoiceLineFiscalResolver.Resolve(
            null, code, VatTreatment.DomesticVat, 21m, TenantDefaultRate);

        Assert.Equal(VatTreatment.ReverseCharge, resolution.Treatment);
        Assert.Equal(FiscalTreatmentSource.SalesCode, resolution.Source);
        Assert.Equal(0m, resolution.RatePercent);
        Assert.Equal("AE", resolution.VatCategoryCode);
        Assert.NotNull(resolution.LegalText);
    }

    [Fact]
    public void ALegacyCategoryOverride_MayRefineADomesticTreatment_WhenCompatible()
    {
        var code = Code();
        code.VatCategoryOverride = "Z";

        var resolution = InvoiceLineFiscalResolver.Resolve(
            null, code, VatTreatment.DomesticVat, 0m, TenantDefaultRate);

        Assert.Equal("Z", resolution.VatCategoryCode);
    }

    [Fact]
    public void ALineOverride_DerivesItsCategoryFromTheOverride_NotFromTheCode()
    {
        var code = Code();
        code.VatCategoryOverride = "S";

        var resolution = InvoiceLineFiscalResolver.Resolve(
            VatTreatment.IntraCommunitySupply, code, VatTreatment.DomesticVat, 21m, TenantDefaultRate);

        Assert.Equal(0m, resolution.RatePercent);
        Assert.Equal("K", resolution.VatCategoryCode);
        Assert.Equal(FiscalTreatmentSource.LineOverride, resolution.Source);
    }

    [Fact]
    public void AStatutoryCodeClassification_DerivesItsCategoryFromTheClassification()
    {
        var code = Code(vatOverride: VatTreatment.ReverseCharge);
        code.VatCategoryOverride = "S";

        var resolution = InvoiceLineFiscalResolver.Resolve(
            null, code, VatTreatment.DomesticVat, 21m, TenantDefaultRate);

        Assert.Equal("AE", resolution.VatCategoryCode);
        Assert.Equal(0m, resolution.RatePercent);
    }
}
