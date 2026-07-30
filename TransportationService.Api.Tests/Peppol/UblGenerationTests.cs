using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Peppol.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Peppol;

/// <summary>Phase 12: UBL BIS 3.0 generation, VAT category mapping, credit notes, validation catalog.</summary>
public class UblGenerationTests
{
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    // --- Pure builder tests (no database) ---

    private static UblDocumentBuilder.UblDocumentInput Input(
        VatTreatment treatment = VatTreatment.DomesticVat,
        decimal vatRate = 21m,
        InvoiceKind kind = InvoiceKind.Invoice,
        string? buyerReference = null,
        string? purchaseOrder = null,
        (decimal Quantity, decimal UnitPrice)[]? lines = null,
        string unitCode = "C62")
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = kind == InvoiceKind.CreditNote ? "CN2026070001" : "2026070001",
            Kind = kind,
            InvoiceDate = new DateOnly(2026, 7, 30),
            DueDate = new DateOnly(2026, 8, 29),
            Currency = "EUR",
            CustomerVatTreatment = treatment.ToString(),
            VatLegalText = VatTreatmentCatalog.Resolve(treatment).InvoiceLegalText,
            SellerName = "Trans BV",
            SellerVatNumber = "BE0123456749",
            SellerIban = "BE68539007547034",
            PurchaseOrderNumber = purchaseOrder,
            PaymentReference = "+++010/1234/56789+++",
        };
        var invoiceLines = (lines ?? [(2m, 100m)]).Select((l, index) => new InvoiceLine
        {
            Id = Guid.NewGuid(), Sequence = index + 1, Description = $"Lijn {index + 1}",
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, VatRatePercent = vatRate, UnitCode = unitCode,
        }).ToList();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), Name = "Acme NV", LegalName = "Acme NV",
            VatNumber = "BE0417497106", PeppolId = "0417497106", PeppolScheme = "0208",
            CountryCode = "BE", BuyerReference = buyerReference,
        };
        var seller = new LegalEntity
        {
            Id = Guid.NewGuid(), LegalName = "Trans BV", VatNumber = "BE0123456749",
            PeppolId = "0123456749", PeppolScheme = "0208", Iban = "BE68539007547034",
            Street = "Kaai", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen", CountryCode = "BE",
        };
        return new UblDocumentBuilder.UblDocumentInput(
            invoice, invoiceLines, customer, seller,
            kind == InvoiceKind.CreditNote ? "2026070001" : null, null);
    }

    [Theory]
    [InlineData(VatTreatment.DomesticVat, 21, "S", null)]
    [InlineData(VatTreatment.DomesticVat, 0, "Z", null)]
    [InlineData(VatTreatment.ReverseCharge, 0, "AE", "VATEX-EU-AE")]
    [InlineData(VatTreatment.IntraCommunitySupply, 0, "K", "VATEX-EU-IC")]
    [InlineData(VatTreatment.ExportOutsideEu, 0, "G", "VATEX-EU-G")]
    [InlineData(VatTreatment.VatExempt, 0, "E", null)]
    public void Build_MapsVatTreatmentToCategoryAndExemption(
        VatTreatment treatment, decimal rate, string expectedCategory, string? expectedReasonCode)
    {
        var xml = XDocument.Parse(UblDocumentBuilder.Build(Input(treatment, rate)));

        var taxCategory = xml.Descendants(Cac + "TaxSubtotal").Single().Element(Cac + "TaxCategory")!;
        Assert.Equal(expectedCategory, taxCategory.Element(Cbc + "ID")!.Value);
        Assert.Equal(expectedReasonCode, taxCategory.Element(Cbc + "TaxExemptionReasonCode")?.Value);

        var expectedText = VatTreatmentCatalog.Resolve(treatment).InvoiceLegalText;
        Assert.Equal(expectedText, taxCategory.Element(Cbc + "TaxExemptionReason")?.Value);

        var lineCategory = xml.Descendants(Cac + "ClassifiedTaxCategory").Single();
        Assert.Equal(expectedCategory, lineCategory.Element(Cbc + "ID")!.Value);
    }

    [Fact]
    public void Build_TotalsReconcile_IncludingHalfCentRounding()
    {
        // 3 × 3.335 = 10.005 → line extension 10.01 (away from zero); VAT 21% of 10.01 = 2.1021 → 2.10.
        var xml = XDocument.Parse(UblDocumentBuilder.Build(Input(lines: [(3m, 3.335m), (1m, 50m)])));

        var monetary = xml.Descendants(Cac + "LegalMonetaryTotal").Single();
        decimal Get(string name) => decimal.Parse(monetary.Element(Cbc + name)!.Value, System.Globalization.CultureInfo.InvariantCulture);

        var lineExtension = Get("LineExtensionAmount");
        var taxExclusive = Get("TaxExclusiveAmount");
        var taxInclusive = Get("TaxInclusiveAmount");
        var payable = Get("PayableAmount");
        var taxTotal = decimal.Parse(
            xml.Descendants(Cac + "TaxTotal").Single().Element(Cbc + "TaxAmount")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(60.01m, lineExtension);
        Assert.Equal(lineExtension, taxExclusive);
        Assert.Equal(12.60m, taxTotal); // 21% of 60.01 = 12.6021 → 12.60
        Assert.Equal(lineExtension + taxTotal, taxInclusive);
        Assert.Equal(taxInclusive, payable);

        // The line extensions in the document sum exactly to the document total.
        var lineSum = xml.Descendants(Cac + "InvoiceLine")
            .Select(l => decimal.Parse(l.Element(Cbc + "LineExtensionAmount")!.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Sum();
        Assert.Equal(lineExtension, lineSum);
    }

    [Fact]
    public void Build_IsDeterministic_ByteEqualAcrossBuilds()
    {
        var input = Input(VatTreatment.ReverseCharge, 0m, lines: [(2m, 100m), (1.5m, 33.33m)]);

        Assert.Equal(UblDocumentBuilder.Build(input), UblDocumentBuilder.Build(input));
    }

    [Fact]
    public void Build_InvoiceShape_RootNamespaceCustomizationAndPayment()
    {
        var xml = XDocument.Parse(UblDocumentBuilder.Build(Input(purchaseOrder: "PO-77")));

        Assert.Equal("Invoice", xml.Root!.Name.LocalName);
        Assert.Equal("urn:oasis:names:specification:ubl:schema:xsd:Invoice-2", xml.Root.Name.NamespaceName);
        Assert.Equal(UblDocumentBuilder.CustomizationId, xml.Root.Element(Cbc + "CustomizationID")!.Value);
        Assert.Equal("380", xml.Root.Element(Cbc + "InvoiceTypeCode")!.Value);
        Assert.Equal("PO-77", xml.Root.Element(Cac + "OrderReference")!.Element(Cbc + "ID")!.Value);

        var payment = xml.Root.Element(Cac + "PaymentMeans")!;
        Assert.Equal("30", payment.Element(Cbc + "PaymentMeansCode")!.Value);
        Assert.Equal("+++010/1234/56789+++", payment.Element(Cbc + "PaymentID")!.Value);
        Assert.Equal("BE68539007547034", payment.Element(Cac + "PayeeFinancialAccount")!.Element(Cbc + "ID")!.Value);

        var endpoint = xml.Descendants(Cac + "AccountingSupplierParty").Single()
            .Descendants(Cbc + "EndpointID").Single();
        Assert.Equal("0208", endpoint.Attribute("schemeID")!.Value);
        Assert.Equal("0123456749", endpoint.Value);
    }

    [Fact]
    public void Build_CreditNoteShape_TypeCodeBillingReferenceNoPayment()
    {
        var xml = XDocument.Parse(UblDocumentBuilder.Build(Input(kind: InvoiceKind.CreditNote)));

        Assert.Equal("CreditNote", xml.Root!.Name.LocalName);
        Assert.Equal("urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2", xml.Root.Name.NamespaceName);
        Assert.Equal("381", xml.Root.Element(Cbc + "CreditNoteTypeCode")!.Value);
        Assert.Null(xml.Root.Element(Cbc + "DueDate"));
        Assert.Null(xml.Root.Element(Cac + "PaymentMeans"));
        Assert.Equal("2026070001",
            xml.Root.Element(Cac + "BillingReference")!.Element(Cac + "InvoiceDocumentReference")!.Element(Cbc + "ID")!.Value);
        Assert.Single(xml.Descendants(Cac + "CreditNoteLine"));
        Assert.Equal("C62", xml.Descendants(Cbc + "CreditedQuantity").Single().Attribute("unitCode")!.Value);
    }

    [Fact]
    public void Build_UnitCodeAndBuyerReferenceRules()
    {
        // Unit code lands on the quantity; customer buyer reference wins over the PO number.
        var withBuyerRef = XDocument.Parse(UblDocumentBuilder.Build(
            Input(buyerReference: "KP-42", purchaseOrder: "PO-77", unitCode: "KGM")));
        Assert.Equal("KGM", withBuyerRef.Descendants(Cbc + "InvoicedQuantity").First().Attribute("unitCode")!.Value);
        Assert.Equal("KP-42", withBuyerRef.Root!.Element(Cbc + "BuyerReference")!.Value);

        // Without a customer buyer reference the PO fills BuyerReference; without both, the invoice number.
        var withPo = XDocument.Parse(UblDocumentBuilder.Build(Input(purchaseOrder: "PO-77")));
        Assert.Equal("PO-77", withPo.Root!.Element(Cbc + "BuyerReference")!.Value);
        var bare = XDocument.Parse(UblDocumentBuilder.Build(Input()));
        Assert.Equal("2026070001", bare.Root!.Element(Cbc + "BuyerReference")!.Value);
    }

    [Theory]
    [InlineData("kg", "KGM")]
    [InlineData("Europallet", "XPX")]
    [InlineData("uur", "HUR")]
    [InlineData("ldm", "MTR")]
    [InlineData("volslagen-onbekend", "C62")]
    [InlineData(null, "C62")]
    public void UnitCodeMap_ResolvesKnownCodesAndFallsBack(string? key, string expected)
        => Assert.Equal(expected, UnEceUnitCodeMap.Resolve(key));

    // --- Credit note service + VAT category freeze (SQLite) ---

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Invoices, PeppolInvoiceService PeppolInvoices, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = "t", CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var invoices = new InvoiceService(db.Context, tenant, audit, clock,
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new AccountingService(db.Context, tenant, audit));
        return new Harness(db, invoices, new PeppolInvoiceService(db.Context, tenant), tenantId);
    }

    private static async Task<(Customer Customer, LegalEntity Entity)> SeedPartiesAsync(
        Harness h, bool peppolReady = true, bool settingsEnabled = true)
    {
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, LegalName = "Trans BV",
            VatNumber = "BE0123456749", Iban = "BE68539007547034", CountryCode = "BE",
            PeppolId = peppolReady ? "0123456749" : null, PeppolScheme = peppolReady ? "0208" : null,
            InvoicePrefix = "F", CreditNotePrefix = null, IsActive = true, IsDefault = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerNumber = "KL-1", Name = "Acme NV",
            VatNumber = "BE0417497106", CountryCode = "BE", IsActive = true,
            PeppolId = peppolReady ? "0417497106" : null, PeppolScheme = peppolReady ? "0208" : null,
            PeppolEnabled = peppolReady, DefaultLegalEntityId = entity.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        h.Db.Context.LegalEntities.Add(entity);
        h.Db.Context.Customers.Add(customer);
        if (settingsEnabled)
        {
            h.Db.Context.PeppolSettings.Add(new PeppolSettings
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, LegalEntityId = entity.Id,
                Enabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }

        await h.Db.Context.SaveChangesAsync();
        return (customer, entity);
    }

    private static async Task<Invoice> SeedSentInvoiceAsync(
        Harness h, Customer customer, LegalEntity entity,
        VatTreatment treatment = VatTreatment.DomesticVat, decimal vatRate = 21m,
        string number = "F2026070001", bool frozen = true)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceNumber = number,
            CustomerId = customer.Id, LegalEntityId = entity.Id,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 7,
            InvoiceDate = new DateOnly(2026, 7, 30), DueDate = new DateOnly(2026, 8, 29),
            Status = InvoiceStatus.Sent, Currency = "EUR",
            SellerName = entity.LegalName, SellerVatNumber = entity.VatNumber, SellerIban = entity.Iban,
            CustomerVatTreatment = treatment.ToString(), CustomerVatNumberSnapshot = customer.VatNumber,
            VatLegalText = VatTreatmentCatalog.Resolve(treatment).InvoiceLegalText,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Lines =
            [
                new InvoiceLine
                {
                    Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "Transport",
                    Quantity = 1m, UnitPrice = 500m, VatRatePercent = vatRate, UnitCode = "C62",
                    VatCategoryCode = frozen ? VatTreatmentCatalog.ResolveVatCategory(treatment, vatRate).Code : null,
                    LedgerAccountNumberSnapshot = frozen ? "700000" : null,
                    SalesCategoryNameSnapshot = frozen ? "Transport" : null,
                },
            ],
        };
        h.Db.Context.Invoices.Add(invoice);
        await h.Db.Context.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task CreditNote_CopiesLinesPositive_LinksAndNumbersWithCnPrefix()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var original = await SeedSentInvoiceAsync(h, customer, entity);

        var result = await h.Invoices.CreateCreditNoteAsync(original.Id, CancellationToken.None);

        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.Success, result.Outcome);
        var creditNote = result.Invoice!;
        Assert.Equal(InvoiceKind.CreditNote, creditNote.Kind);
        Assert.Equal(original.Id, creditNote.CreditedInvoiceId);
        Assert.Equal(original.InvoiceNumber, creditNote.CreditedInvoiceNumber);
        Assert.Equal(InvoiceStatus.Draft, creditNote.Status);
        Assert.StartsWith("FCN", creditNote.InvoiceNumber); // CreditNotePrefix null → InvoicePrefix + "CN"
        var line = Assert.Single(creditNote.Lines);
        Assert.True(line.UnitPrice > 0);
        Assert.True(line.Quantity > 0);
        Assert.Null(line.TransportOrderId);
        Assert.Equal("S", line.VatCategoryCode); // copied frozen snapshot

        // A second live credit note for the same invoice is refused.
        var duplicate = await h.Invoices.CreateCreditNoteAsync(original.Id, CancellationToken.None);
        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.InvalidState, duplicate.Outcome);

        // A credit note itself can never be credited.
        var creditOfCredit = await h.Invoices.CreateCreditNoteAsync(creditNote.Id, CancellationToken.None);
        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.InvalidState, creditOfCredit.Outcome);
    }

    [Fact]
    public async Task CreditNote_RequiresSentOrPaidOriginal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var draft = await SeedSentInvoiceAsync(h, customer, entity);
        draft.Status = InvoiceStatus.Draft;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Invoices.CreateCreditNoteAsync(draft.Id, CancellationToken.None);

        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Send_FreezesVatCategoryPerLine_FromTreatmentSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity,
            VatTreatment.ReverseCharge, vatRate: 0m, frozen: false);
        invoice.Status = InvoiceStatus.Draft;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.Success, result.Outcome);
        var stored = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == invoice.Id);
        Assert.Equal("AE", stored.VatCategoryCode);
    }

    // --- Validation catalog ---

    [Fact]
    public async Task Validate_CompleteSentInvoice_IsValid()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var result = await h.PeppolInvoices.ValidateAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsValid, string.Join(" | ", result.Issues.Select(i => i.Message)));
    }

    [Fact]
    public async Task Validate_FlagsMissingConfigurationCustomerAndStateIssues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h, peppolReady: false, settingsEnabled: false);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity, frozen: false);
        invoice.Status = InvoiceStatus.Draft;
        invoice.Currency = "EU";
        await h.Db.Context.SaveChangesAsync();

        var result = await h.PeppolInvoices.ValidateAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        var codes = result.Issues.Select(i => i.Code).ToList();
        Assert.Contains("company.peppol_identity", codes);
        Assert.Contains("company.peppol_disabled", codes);
        Assert.Contains("customer.peppol_identity", codes);
        Assert.Contains("customer.peppol_disabled", codes);
        Assert.Contains("invoice.not_finalized", codes);
        Assert.Contains("invoice.currency", codes);
        // Every message is Dutch and actionable (no raw codes leaking into text).
        Assert.All(result.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
    }

    [Fact]
    public async Task Validate_NegativeInvoiceTotal_PointsToCreditNote()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        var line = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == invoice.Id);
        line.UnitPrice = -500m;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.PeppolInvoices.ValidateAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(result);
        var issue = result.Issues.Single(i => i.Code == "totals.negative");
        Assert.Contains("creditnota", issue.Message);
    }

    [Fact]
    public async Task Validate_ReverseChargeWithoutVatNumber_Flags()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity, VatTreatment.ReverseCharge, 0m);
        invoice.CustomerVatNumberSnapshot = null;
        customer.VatNumber = null;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.PeppolInvoices.ValidateAsync(invoice.Id, CancellationToken.None);

        Assert.Contains(result!.Issues, i => i.Code == "customer.vat");
    }

    [Fact]
    public async Task Validate_ForeignTenantInvoice_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var foreign = new PeppolInvoiceService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));
        Assert.Null(await foreign.ValidateAsync(invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetXml_ValidInvoice_RendersDocument_InvalidReturnsIssues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var valid = await SeedSentInvoiceAsync(h, customer, entity);

        var success = await h.PeppolInvoices.GetXmlAsync(valid.Id, CancellationToken.None);
        Assert.NotNull(success);
        Assert.NotNull(success.Value.Xml);
        Assert.Null(success.Value.Failure);
        var xml = XDocument.Parse(success.Value.Xml!);
        Assert.Equal("Invoice", xml.Root!.Name.LocalName);
        Assert.Equal(valid.InvoiceNumber, xml.Root.Element(Cbc + "ID")!.Value);

        var draft = await SeedSentInvoiceAsync(h, customer, entity, number: "F2026070002");
        draft.Status = InvoiceStatus.Draft;
        await h.Db.Context.SaveChangesAsync();
        var failure = await h.PeppolInvoices.GetXmlAsync(draft.Id, CancellationToken.None);
        Assert.NotNull(failure);
        Assert.Null(failure.Value.Xml);
        Assert.False(failure.Value.Failure!.IsValid);
    }

    [Fact]
    public void Build_EmitsXmlDeclarationAndInvoicePeriod()
    {
        var raw = UblDocumentBuilder.Build(Input(VatTreatment.IntraCommunitySupply, 0m));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", raw);
        var xml = XDocument.Parse(raw);
        var period = xml.Root!.Element(Cac + "InvoicePeriod");
        Assert.NotNull(period);
        // BR-IC-11: intra-community documents must carry an invoicing period.
        Assert.Equal("2026-07-01", period.Element(Cbc + "StartDate")!.Value);
        Assert.Equal("2026-07-31", period.Element(Cbc + "EndDate")!.Value);
    }

    [Fact]
    public void Build_RefusesMissingParticipantIdentity()
    {
        var input = Input();
        input.Customer.PeppolId = null;

        Assert.Throws<InvalidOperationException>(() => UblDocumentBuilder.Build(input));
    }

    [Fact]
    public async Task CreditNote_XmlEndToEnd_CarriesBillingReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var original = await SeedSentInvoiceAsync(h, customer, entity);
        var created = await h.Invoices.CreateCreditNoteAsync(original.Id, CancellationToken.None);
        var creditNoteId = created.Invoice!.Id;
        // Finalize the credit note so validation passes, keeping its copied snapshots.
        var stored = await h.Db.Context.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == creditNoteId);
        stored.Status = InvoiceStatus.Sent;
        foreach (var line in stored.Lines)
        {
            line.LedgerAccountNumberSnapshot = "700000";
        }

        await h.Db.Context.SaveChangesAsync();

        var result = await h.PeppolInvoices.GetXmlAsync(creditNoteId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Value.Xml);
        var xml = XDocument.Parse(result.Value.Xml!);
        Assert.Equal("CreditNote", xml.Root!.Name.LocalName);
        Assert.Equal(original.InvoiceNumber,
            xml.Root.Element(Cac + "BillingReference")!.Element(Cac + "InvoiceDocumentReference")!.Element(Cbc + "ID")!.Value);
    }

    [Fact]
    public async Task CreditNote_SharesMonthlySequenceWithInvoices()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var original = await SeedSentInvoiceAsync(h, customer, entity);

        var creditNote = (await h.Invoices.CreateCreditNoteAsync(original.Id, CancellationToken.None)).Invoice!;

        // Same (entity, 2026-07) sequence row advanced; the CN prefix keeps numbers distinct.
        var sequence = await h.Db.Context.InvoiceSequences.SingleAsync(
            s => s.LegalEntityId == entity.Id && s.Year == 2026 && s.Month == 7);
        Assert.Equal(2, sequence.NextValue);
        Assert.NotEqual(original.InvoiceNumber, creditNote.InvoiceNumber);
    }

    [Fact]
    public async Task Send_NeverOverwritesFrozenVatCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity); // frozen S at 21%
        // Simulate a later treatment change + a bounce back through Draft.
        invoice.Status = InvoiceStatus.Draft;
        invoice.CustomerVatTreatment = VatTreatment.ReverseCharge.ToString();
        await h.Db.Context.SaveChangesAsync();

        await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        var line = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == invoice.Id);
        Assert.Equal("S", line.VatCategoryCode); // frozen value untouched
    }

    [Fact]
    public async Task Send_WithoutTreatmentSnapshot_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity, frozen: false);
        invoice.Status = InvoiceStatus.Draft;
        invoice.CustomerVatTreatment = null;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.InvalidState, result.Outcome);
        Assert.Contains("btw-regeling", result.Error);
    }

    [Fact]
    public async Task Validate_FlagsNegativePriceLine_PoPolicy_CategoryRateAndLedgerGaps()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        customer.PurchaseOrderPolicy = PurchaseOrderPolicy.Required;
        var invoice = await SeedSentInvoiceAsync(h, customer, entity, VatTreatment.ReverseCharge, vatRate: 21m, frozen: false);
        h.Db.Context.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoice.Id, Sequence = 2,
            Description = "Korting", Quantity = 1m, UnitPrice = -50m, VatRatePercent = 0m, UnitCode = "C62",
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.PeppolInvoices.ValidateAsync(invoice.Id, CancellationToken.None);

        var codes = result!.Issues.Select(i => i.Code).ToList();
        Assert.Contains("lines.negative_price", codes);   // BR-27
        Assert.Contains("lines.category_rate", codes);    // AE with 21% rate
        Assert.Contains("invoice.po_required", codes);    // customer PO policy
        Assert.Contains("accounting.ledger", codes);      // Sent without frozen ledger account
    }

    [Fact]
    public async Task Preview_ReportsPartiesGroupsAndTotals()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var preview = await h.PeppolInvoices.PreviewAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal("0208:0123456749", preview.Seller.Participant);
        Assert.Equal("0208:0417497106", preview.Buyer.Participant);
        var group = Assert.Single(preview.VatGroups);
        Assert.Equal("S", group.VatCategoryCode);
        Assert.Equal(500m, preview.Subtotal);
        Assert.Equal(105m, preview.VatAmount);
        Assert.Equal(605m, preview.Total);
    }
}
