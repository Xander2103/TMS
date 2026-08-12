using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Wave 2 §3 (scenario 18 subset): the customer's language is actually consumed. The document
/// language freezes on the invoice at creation (InvoiceLanguageCode ?? DefaultLanguageCode ??
/// nl), a credit note mirrors the credited document, the PDF catalog serves FACTURE/INVOICE/
/// RECHNUNG with per-locale date formats, and the built-in message templates resolve FR/EN for
/// the customer-facing kinds with Dutch as the safety net.
/// </summary>
public class InvoiceLanguageTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Invoices, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync(string? invoiceLanguage, string? defaultLanguage)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Client SA", IsActive = true,
            InvoiceLanguageCode = invoiceLanguage, DefaultLanguageCode = defaultLanguage,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new Modules.Accounting.Services.AccountingService(db.Context, tenant, audit));
        return new Harness(db, invoices, tenantId, customerId);
    }

    private static Task<InvoiceOperationResult> CreateAsync(Harness h) =>
        h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Prestation", 1m, 100m, 21m)], null),
            CancellationToken.None);

    [Theory]
    [InlineData("fr", "nl", "fr")]  // explicit invoice language wins
    [InlineData(null, "fr", "fr")]  // falls back to the customer's general language
    [InlineData(null, null, "nl")]  // nl is the final default
    public async Task Create_FreezesTheDocumentLanguage(string? invoiceLanguage, string? defaultLanguage, string expected)
    {
        var h = await SeedAsync(invoiceLanguage, defaultLanguage);
        using var _ = h.Db;

        var created = await CreateAsync(h);

        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var stored = await h.Db.Context.Invoices.SingleAsync(i => i.Id == created.Invoice!.Id);
        Assert.Equal(expected, stored.LanguageCode);
    }

    [Fact]
    public async Task CreditNote_MirrorsTheCreditedDocumentLanguage_EvenAfterCustomerChanges()
    {
        var h = await SeedAsync("fr", null);
        using var _ = h.Db;
        var created = await CreateAsync(h);
        var sent = await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);

        // Master-data edits never move history: switch the customer to English first.
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        customer.InvoiceLanguageCode = "en";
        await h.Db.Context.SaveChangesAsync();

        var credit = await h.Invoices.CreateCreditNoteAsync(created.Invoice!.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, credit.Outcome);
        var stored = await h.Db.Context.Invoices.SingleAsync(i => i.Id == credit.Invoice!.Id);
        Assert.Equal("fr", stored.LanguageCode);
    }

    // --- PDF string catalog -------------------------------------------------------------------

    [Theory]
    [InlineData("fr", "FACTURE", "NOTE DE CRÉDIT", "dd/MM/yyyy")]
    [InlineData("en", "INVOICE", "CREDIT NOTE", "dd/MM/yyyy")]
    [InlineData("de", "RECHNUNG", "GUTSCHRIFT", "dd.MM.yyyy")]
    [InlineData("nl", "FACTUUR", "CREDITNOTA", "dd-MM-yyyy")]
    [InlineData(null, "FACTUUR", "CREDITNOTA", "dd-MM-yyyy")]
    [InlineData("xx", "FACTUUR", "CREDITNOTA", "dd-MM-yyyy")]
    public void PdfStrings_ServeTheDocumentLanguage_WithDutchAsFallback(
        string? code, string title, string creditTitle, string dateFormat)
    {
        var strings = InvoicePdfStrings.For(code);
        Assert.Equal(title, strings.Title);
        Assert.Equal(creditTitle, strings.CreditNoteTitle);
        Assert.Equal(dateFormat, strings.DateFormat);
    }

    [Fact]
    public void Renderer_RendersAFrenchInvoice_NoException()
    {
        var bytes = InvoicePdfRenderer.Render(new InvoicePdfSnapshot(
            "Acme Transport BV", "Havenlaan 1, 2000 Antwerpen", "BE0123456789",
            "BE68539007547034", "GKCCBEBB", null, null, null,
            "2026080001", new DateOnly(2026, 8, 12), new DateOnly(2026, 9, 11),
            "Client SA", "Rue du Port 5, 4000 Liège", "BE0987654321", null,
            [new InvoicePdfLine("Transport Anvers-Liège", 1, 450.00m, 21m, 450.00m)],
            450.00m, 94.50m, 544.50m, "EUR", null,
            LanguageCode: "fr"));

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // --- Built-in message templates -----------------------------------------------------------

    [Fact]
    public void BuiltInTemplates_ResolveFrenchAndEnglish_ForCustomerFacingKinds()
    {
        var fr = BuiltInMessageTemplates.Resolve(MessageKinds.InvoiceSent, MessageChannel.Email, "fr");
        Assert.Contains("Facture", fr.Subject);
        Assert.Contains("Cordialement", fr.Body);

        var en = BuiltInMessageTemplates.Resolve(MessageKinds.EtaUpdate, MessageChannel.Email, "en");
        Assert.Contains("expected arrival", en.Subject!, StringComparison.OrdinalIgnoreCase);

        var smsFr = BuiltInMessageTemplates.Resolve(MessageKinds.DeliveryCompleted, MessageChannel.Sms, "fr");
        Assert.Contains("livraison", smsFr.Body);
    }

    [Fact]
    public void BuiltInTemplates_FallBackToDutch_ForUntranslatedKindsAndLanguages()
    {
        // HR kind has no FR content → Dutch.
        var hrKind = BuiltInMessageTemplates.Resolve(MessageKinds.LeaveApproved, MessageChannel.Email, "fr");
        Assert.Contains("goedgekeurd", hrKind.Subject!);

        // German is deferred → Dutch.
        var german = BuiltInMessageTemplates.Resolve(MessageKinds.InvoiceSent, MessageChannel.Email, "de");
        Assert.Contains("Factuur", german.Subject!);

        // No language (existing call sites) behaves exactly as before.
        var legacy = BuiltInMessageTemplates.Resolve(MessageKinds.InvoiceSent, MessageChannel.Email);
        Assert.Contains("Factuur", legacy.Subject!);
    }
}
