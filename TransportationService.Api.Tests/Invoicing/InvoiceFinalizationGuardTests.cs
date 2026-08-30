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

    /// <summary>
    /// The mirror of <see cref="Cancel_DraftInvoice_WithTransmissionPastTheQueue_IsRefused"/>:
    /// deleting a Draft DOES release its orders, so the same adversarial state must be refused on
    /// the delete path too, or the guard is bypassed by pressing "Verwijderen" instead.
    /// </summary>
    [Fact]
    public async Task Delete_DraftInvoice_WithTransmissionPastTheQueue_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var snapshotId = await AddPricingSnapshotAsync(h);
        var created = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        await AddTransmissionAsync(h, created.Invoice!.Id, PeppolTransmissionStatus.SubmittedToProvider);

        var refused = await h.Sut.DeleteAsync(created.Invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
        Assert.NotNull(await h.Db.Context.Invoices.FindAsync(created.Invoice.Id));
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        Assert.Equal(OrderPricingStatus.Invoiced,
            (await h.Db.Context.TransportOrderPricingSnapshots.FindAsync(snapshotId))!.Status);
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

    /// <summary>
    /// The behaviour that is uniquely the credit-note freeze skip's: the GAP FILL. Both freeze
    /// methods are idempotent, so a line that already carries a ledger snapshot is safe either
    /// way — but a credited line whose category had NO ledger account at Send carries a null, and
    /// re-freezing would fill it from today's mapping. The credit would then book against an
    /// account the invoice it reverses never touched.
    /// </summary>
    [Fact]
    public async Task CreditNote_NeverGapFillsALedgerAccountTheCreditedLineNeverHad()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Sent while the category existed but the MAPPING was still missing.
        var category = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None))
            .Single(c => c.Code == "DIVERS-BINNEN");
        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, category.Id)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        var originalLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == created.Invoice.Id);
        Assert.Null(originalLine.LedgerAccountNumberSnapshot);
        Assert.NotNull(originalLine.VatTreatmentSnapshot);

        // The category gets its account only AFTER the invoice went out.
        var account = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest("700400", "Diverse verkoop binnenland"), CancellationToken.None);
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, account.Id, true, category.SortOrder), CancellationToken.None);

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        var sentCredit = await h.Sut.ChangeStatusAsync(credit.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sentCredit.Outcome);

        var creditLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == credit.Invoice.Id);
        Assert.Null(creditLine.LedgerAccountNumberSnapshot);
        Assert.Null(creditLine.LedgerAccountNameSnapshot);
        Assert.Null(creditLine.LedgerAccountId);
        Assert.Equal(originalLine.SalesCategoryNameSnapshot, creditLine.SalesCategoryNameSnapshot);
    }

    /// <summary>
    /// The skip is per mirrored LINE, not per document: a line the user adds to a draft credit note
    /// has nothing to mirror and must still freeze its own snapshots at Send, or it would reach the
    /// accounting export without a ledger account.
    /// </summary>
    [Fact]
    public async Task CreditNote_ALineAddedToTheDraft_StillFreezesItsOwnSnapshots()
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
        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        var mirrored = credit.Invoice!.Lines.Single();

        var updated = await h.Sut.UpdateAsync(credit.Invoice.Id, new UpdateInvoiceRequest(
            credit.Invoice.InvoiceDate, credit.Invoice.DueDate,
            [
                new UpdateInvoiceLineInput(mirrored.Id, mirrored.Description, mirrored.Quantity, mirrored.UnitPrice,
                    mirrored.VatRatePercent, category.Id),
                new UpdateInvoiceLineInput(null, "Extra creditlijn", 1m, 10m, 21m, category.Id),
            ], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, updated.Outcome);

        await h.Sut.ChangeStatusAsync(credit.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        var addedLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.Description == "Extra creditlijn");
        Assert.Equal("700400", addedLine.LedgerAccountNumberSnapshot);
        Assert.NotNull(addedLine.VatTreatmentSnapshot);
    }

    /// <summary>Maps DIVERS-BINNEN to a ledger account and returns the category id.</summary>
    private static async Task<Guid> MapDiversBinnenAsync(Harness h, string accountNumber, string accountName)
    {
        var account = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest(accountNumber, accountName), CancellationToken.None);
        var category = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None))
            .Single(c => c.Code == "DIVERS-BINNEN");
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, account.Id, true, category.SortOrder),
            CancellationToken.None);
        return category.Id;
    }

    /// <summary>
    /// Makes the sales code carry a statutory classification, the way a master-data correction would
    /// AFTER the invoice went out. Any line re-derived from live data afterwards lands on
    /// reverse charge at 0% / category "AE" instead of the 21% / "S" that was invoiced.
    /// </summary>
    private static async Task ReclassifyCodeAsReverseChargeAsync(Harness h, Guid categoryId)
    {
        var stored = await h.Db.Context.SalesCategories.SingleAsync(c => c.Id == categoryId);
        stored.VatTreatmentOverride = VatTreatment.ReverseCharge;
        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>
    /// Legacy shape: `VatTreatmentSnapshot` and the rest of the sprint-5H block only exist since
    /// 2026-08-28 and were never backfilled, so a line frozen before that carries a ledger snapshot
    /// with a NULL treatment snapshot. Its credit-note copy inherits that null and must still be
    /// recognised as a mirror — otherwise the credit note re-derives VAT treatment, rate, category
    /// and cost centre from today's master data and credits 0% against an invoice that charged 21%.
    /// </summary>
    [Fact]
    public async Task CreditNote_MirrorsALegacyCreditedLine_ThatCarriesNoTreatmentSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapDiversBinnenAsync(h, "700400", "Diverse verkoop binnenland");
        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, categoryId)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        // Rewrite the sent line into the pre-5H shape: ledger snapshot kept, fiscal block null.
        var originalLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == created.Invoice.Id);
        Assert.Equal("700400", originalLine.LedgerAccountNumberSnapshot);
        originalLine.VatTreatmentSnapshot = null;
        originalLine.VatTreatmentSourceSnapshot = null;
        originalLine.VatLegalTextSnapshot = null;
        originalLine.SalesCodeSnapshot = null;
        originalLine.DescriptionLanguageSnapshot = null;
        originalLine.CostCentreSnapshot = null;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(21m, originalLine.VatRatePercent);
        Assert.Equal("S", originalLine.VatCategoryCode);

        await ReclassifyCodeAsReverseChargeAsync(h, categoryId);

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, credit.Outcome);
        // The draft view already shows the mirror, not today's live mapping.
        Assert.Equal("700400", credit.Invoice!.Lines.Single().LedgerAccountNumber);

        var sent = await h.Sut.ChangeStatusAsync(credit.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);

        var creditLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == credit.Invoice.Id);
        Assert.Equal(21m, creditLine.VatRatePercent);
        Assert.Equal("S", creditLine.VatCategoryCode);
        Assert.Equal("700400", creditLine.LedgerAccountNumberSnapshot);
        Assert.Equal("Verkoop binnenland", creditLine.Description);
        // The credited line had no fiscal block; the mirror honestly has none either.
        Assert.Null(creditLine.VatTreatmentSnapshot);
        Assert.Null(creditLine.VatLegalTextSnapshot);
        Assert.Null(creditLine.CostCentreSnapshot);
    }

    /// <summary>
    /// The oldest shape of all: a credited line with no fiscal freeze whatsoever (pre-Peppol, so not
    /// even a UBL category). The copy is stamped with the category the credited HEADER dictates —
    /// exactly what Send would have written — so it still reads as a mirror and is never re-derived.
    /// </summary>
    [Fact]
    public async Task CreditNote_MirrorsAnAncientCreditedLine_WithNoFiscalSnapshotsAtAll()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapDiversBinnenAsync(h, "700400", "Diverse verkoop binnenland");
        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, categoryId)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var originalLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == created.Invoice.Id);
        originalLine.VatTreatmentSnapshot = null;
        originalLine.VatTreatmentSourceSnapshot = null;
        originalLine.VatLegalTextSnapshot = null;
        originalLine.SalesCodeSnapshot = null;
        originalLine.DescriptionLanguageSnapshot = null;
        originalLine.CostCentreSnapshot = null;
        originalLine.SalesCategoryNameSnapshot = null;
        originalLine.LedgerAccountId = null;
        originalLine.LedgerAccountNumberSnapshot = null;
        originalLine.LedgerAccountNameSnapshot = null;
        originalLine.VatCategoryCode = null;
        await h.Db.Context.SaveChangesAsync();

        await ReclassifyCodeAsReverseChargeAsync(h, categoryId);

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(credit.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var creditLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == credit.Invoice.Id);
        Assert.Equal(21m, creditLine.VatRatePercent);
        Assert.Equal("S", creditLine.VatCategoryCode);
        Assert.Null(creditLine.VatTreatmentSnapshot);
        Assert.Null(creditLine.LedgerAccountNumberSnapshot);
    }

    /// <summary>The M-4 refusal must recognise a legacy mirror too, not only a post-5H one.</summary>
    [Fact]
    public async Task CreditNote_RecategorisingALegacyMirroredLine_IsAlsoRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapDiversBinnenAsync(h, "700400", "Diverse verkoop binnenland");
        var otherCategory = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None))
            .First(c => c.Id != categoryId);
        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, categoryId)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        var originalLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.InvoiceId == created.Invoice.Id);
        originalLine.VatTreatmentSnapshot = null;
        await h.Db.Context.SaveChangesAsync();

        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        var mirrored = credit.Invoice!.Lines.Single();

        var refused = await h.Sut.UpdateAsync(credit.Invoice.Id, new UpdateInvoiceRequest(
            credit.Invoice.InvoiceDate, credit.Invoice.DueDate,
            [new UpdateInvoiceLineInput(mirrored.Id, mirrored.Description, mirrored.Quantity, mirrored.UnitPrice,
                mirrored.VatRatePercent, otherCategory.Id)], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, refused.Outcome);
        Assert.Equal(categoryId, (await h.Db.Context.InvoiceLines.SingleAsync(l => l.Id == mirrored.Id)).SalesCategoryId);
    }

    /// <summary>
    /// Recategorising a mirrored credit-note line would leave SalesCategoryId pointing at one code
    /// and the frozen snapshots at another. The mirror wins and the edit is refused out loud —
    /// the same rule the header already follows (a credit note is never re-snapshotted).
    /// </summary>
    [Fact]
    public async Task CreditNote_RecategorisingAMirroredLine_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categories = await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None);
        var category = categories.Single(c => c.Code == "DIVERS-BINNEN");
        var otherCategory = categories.First(c => c.Id != category.Id);

        var created = await h.Sut.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, category.Id)], null),
            CancellationToken.None);
        await h.Sut.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        var credit = await h.Sut.CreateCreditNoteAsync(created.Invoice.Id, CancellationToken.None);
        var mirrored = credit.Invoice!.Lines.Single();

        var refused = await h.Sut.UpdateAsync(credit.Invoice.Id, new UpdateInvoiceRequest(
            credit.Invoice.InvoiceDate, credit.Invoice.DueDate,
            [new UpdateInvoiceLineInput(mirrored.Id, mirrored.Description, mirrored.Quantity, mirrored.UnitPrice,
                mirrored.VatRatePercent, otherCategory.Id)], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains("creditnota", refused.Error!, StringComparison.OrdinalIgnoreCase);
        var stored = await h.Db.Context.InvoiceLines.SingleAsync(l => l.Id == mirrored.Id);
        Assert.Equal(category.Id, stored.SalesCategoryId);

        // Editing the amount of that same line stays possible (partial credit).
        var partial = await h.Sut.UpdateAsync(credit.Invoice.Id, new UpdateInvoiceRequest(
            credit.Invoice.InvoiceDate, credit.Invoice.DueDate,
            [new UpdateInvoiceLineInput(mirrored.Id, mirrored.Description, mirrored.Quantity, 40m,
                mirrored.VatRatePercent, category.Id)], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, partial.Outcome);
        Assert.Equal(40m, partial.Invoice!.Lines.Single().UnitPrice);
    }
}
