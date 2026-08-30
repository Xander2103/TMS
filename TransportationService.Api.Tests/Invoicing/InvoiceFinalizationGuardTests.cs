using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// H-06 — financial integrity of a finalized invoice. "Finalized" = the document left the
/// building: its own status is Sent or Paid, OR a Peppol transmission for it got past the local
/// queue (SubmittedToProvider and beyond — the provider has seen it). A finalized document can
/// never be cancelled, so the orders on it can never return from Invoiced to Completed and their
/// pricing snapshots can never leave Invoiced. Correction runs through a credit note, which
/// mirrors the credited document's frozen fiscal data instead of re-freezing live master data.
/// </summary>
public class InvoiceFinalizationGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Sut, AccountingService Accounting,
        Guid TenantId, Guid CustomerId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            VatNumber = "BE0123456789", IsActive = true,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 8, 20), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 1450m,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var accounting = new AccountingService(db.Context, tenant, audit);
        var sut = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            accounting);
        return new Harness(db, sut, accounting, tenantId, customerId, orderId);
    }

    private static async Task<Guid> AddPricingSnapshotAsync(Harness h)
    {
        var snapshotId = Guid.NewGuid();
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = snapshotId, TenantId = h.TenantId, TransportOrderId = h.OrderId,
            TariffDate = new DateOnly(2026, 8, 20), Currency = "EUR",
            Status = OrderPricingStatus.Locked,
        });
        await h.Db.Context.SaveChangesAsync();
        return snapshotId;
    }

    private static async Task AddTransmissionAsync(Harness h, Guid invoiceId, PeppolTransmissionStatus status)
    {
        h.Db.Context.PeppolTransmissions.Add(new PeppolTransmission
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, Status = status,
            SellerParticipant = "0208:0123456789", BuyerParticipant = "0208:0987654321",
        });
        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>Cancelling a sent invoice is refused; the orders and their prices stay untouchable.</summary>
    [Fact]
    public async Task Cancel_SentInvoice_IsRefused_AndKeepsOrdersAndPricingInvoiced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var snapshotId = await AddPricingSnapshotAsync(h);
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var refused = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.Contains("creditnota", refused.Error!, StringComparison.OrdinalIgnoreCase);
        var invoice = await h.Db.Context.Invoices.FindAsync(created.Invoice.Id);
        Assert.Equal(InvoiceStatus.Sent, invoice!.Status);
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Equal(OrderPricingStatus.Invoiced,
            (await h.Db.Context.TransportOrderPricingSnapshots.FindAsync(snapshotId))!.Status);
        // The order stays off the billable list — no second invoice can be built from it.
        Assert.Empty(await h.Sut.ListUninvoicedOrdersAsync(h.CustomerId, CancellationToken.None));
    }

    /// <summary>A delivered Peppol document is the hardest possible proof; cancelling stays refused.</summary>
    [Fact]
    public async Task Cancel_SentInvoice_WithDeliveredTransmission_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        await AddTransmissionAsync(h, created.Invoice.Id, PeppolTransmissionStatus.Delivered);

        var refused = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Equal(PeppolTransmissionStatus.Delivered,
            (await h.Db.Context.PeppolTransmissions.SingleAsync()).Status);
    }

    /// <summary>
    /// Adversarial: a Draft invoice whose document already reached the provider (data written
    /// around the API, or a status rolled back by an older build) is finalized all the same.
    /// </summary>
    [Fact]
    public async Task Cancel_DraftInvoice_WithTransmissionPastTheQueue_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await AddTransmissionAsync(h, created.Invoice!.Id, PeppolTransmissionStatus.SubmittedToProvider);

        var refused = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Cancel_PaidInvoice_IsRefused_WithTheCreditNoteHint()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Paid, CancellationToken.None);

        var refused = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.Contains("creditnota", refused.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InvoiceStatus.Paid, (await h.Db.Context.Invoices.FindAsync(created.Invoice.Id))!.Status);
    }

    /// <summary>The UI reads the allowed transitions; cancel is no longer among them once sent.</summary>
    [Fact]
    public async Task AllowedTransitions_OfASentInvoice_NoLongerOfferCancel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        Assert.Contains(InvoiceStatus.Cancelled, created.Invoice!.AllowedTransitions);

        var sent = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal([InvoiceStatus.Paid], sent.Invoice!.AllowedTransitions);
    }

    /// <summary>A draft was never finalized: cancelling it still releases its orders and prices.</summary>
    [Fact]
    public async Task Cancel_DraftInvoice_StillReleasesOrdersAndPricing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var snapshotId = await AddPricingSnapshotAsync(h);
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        var cancelled = await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, cancelled.Outcome);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Equal(OrderPricingStatus.Locked,
            (await h.Db.Context.TransportOrderPricingSnapshots.FindAsync(snapshotId))!.Status);
    }

    /// <summary>A queued transmission on a cancelled draft is still withdrawn with it.</summary>
    [Fact]
    public async Task Cancel_DraftInvoice_WithQueuedTransmission_WithdrawsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await AddTransmissionAsync(h, created.Invoice!.Id, PeppolTransmissionStatus.Queued);

        var cancelled = await h.Sut.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, cancelled.Outcome);
        Assert.Equal(PeppolTransmissionStatus.Cancelled, (await h.Db.Context.PeppolTransmissions.SingleAsync()).Status);
    }

    /// <summary>
    /// Legacy rows: a cancelled invoice that carries the traces only Send leaves behind must not
    /// be deletable — deleting it would release its orders after the fact.
    /// </summary>
    [Fact]
    public async Task Delete_CancelledInvoiceThatWasOnceFinalized_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        // Simulate the pre-fix data state: sent, then cancelled by an older build.
        var stored = await h.Db.Context.Invoices.FindAsync(created.Invoice.Id);
        stored!.Status = InvoiceStatus.Cancelled;
        await h.Db.Context.SaveChangesAsync();

        var refused = await h.Sut.DeleteAsync(created.Invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.NotNull(await h.Db.Context.Invoices.FindAsync(created.Invoice.Id));
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    /// <summary>A cancelled draft was never finalized and stays deletable.</summary>
    [Fact]
    public async Task Delete_CancelledDraftInvoice_StillWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        var deleted = await h.Sut.DeleteAsync(created.Invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, deleted.Outcome);
    }

    /// <summary>The same order twice on one invoice is double billing, not a valid selection.</summary>
    [Fact]
    public async Task Create_WithTheSameOrderTwice_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId, h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Empty(await h.Db.Context.Invoices.ToListAsync());
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    /// <summary>
    /// A credit note mirrors the fiscal data of the document it credits. Re-mapping the sales
    /// code afterwards must never leak into the credit note — not at creation, not at send.
    /// </summary>
    [Fact]
    public async Task CreditNote_KeepsTheCreditedLineSnapshots_AfterASalesCodeRemap()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var account = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest("700400", "Diverse verkoop binnenland"), CancellationToken.None);
        var category = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None))
            .Single(c => c.Code == "DIVERS-BINNEN");
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, account.Id, true, category.SortOrder), CancellationToken.None);

        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, category.Id)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        var originalLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == created.Invoice.Id);
        Assert.Equal("700400", originalLine.LedgerAccountNumberSnapshot);

        // The mapping moves AFTER the invoice was sent.
        var newAccount = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest("709999", "Nieuw nummer"), CancellationToken.None);
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, newAccount.Id, true, category.SortOrder), CancellationToken.None);

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, credit.Outcome);
        var sentCredit = await h.Sut.ChangeStatusAsync(credit.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sentCredit.Outcome);

        var creditLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == credit.Invoice.Id);
        Assert.Equal("700400", creditLine.LedgerAccountNumberSnapshot);
        Assert.Equal(originalLine.LedgerAccountId, creditLine.LedgerAccountId);
        Assert.Equal(originalLine.SalesCategoryNameSnapshot, creditLine.SalesCategoryNameSnapshot);
        Assert.Equal(originalLine.SalesCodeSnapshot, creditLine.SalesCodeSnapshot);
        Assert.Equal(originalLine.VatTreatmentSnapshot, creditLine.VatTreatmentSnapshot);
        Assert.Equal(originalLine.VatTreatmentSourceSnapshot, creditLine.VatTreatmentSourceSnapshot);
        Assert.Equal(originalLine.VatLegalTextSnapshot, creditLine.VatLegalTextSnapshot);
        Assert.Equal(originalLine.CostCentreSnapshot, creditLine.CostCentreSnapshot);
        Assert.Equal(originalLine.VatCategoryCode, creditLine.VatCategoryCode);
        Assert.Equal(originalLine.VatRatePercent, creditLine.VatRatePercent);
        Assert.Equal(originalLine.Description, creditLine.Description);
    }

    /// <summary>
    /// The credit-note flow end to end: created from a sent invoice, never order-linked (so the
    /// orders stay Invoiced), and blocked while a live one already exists.
    /// </summary>
    [Fact]
    public async Task CreditNote_FromSentInvoice_LeavesTheOrdersInvoiced_AndIsOnlyIssuedOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var snapshotId = await AddPricingSnapshotAsync(h);
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, credit.Outcome);
        Assert.Equal(InvoiceKind.CreditNote, credit.Invoice!.Kind);
        Assert.Equal(created.Invoice.Id, credit.Invoice.CreditedInvoiceId);
        Assert.All(credit.Invoice.Lines, line => Assert.Null(line.TransportOrderId));
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Equal(OrderPricingStatus.Invoiced,
            (await h.Db.Context.TransportOrderPricingSnapshots.FindAsync(snapshotId))!.Status);

        var second = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.InvalidState, second.Outcome);
    }
}
