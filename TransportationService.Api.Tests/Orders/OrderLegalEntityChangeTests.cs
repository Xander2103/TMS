using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Sprint 6 (completion) — the explicit invoicing-entity action on an order: allowed set is a
/// hard boundary, deviating from the customer default is an audited override with a reason,
/// sent invoices are immutable, and concept invoices of the old entity never keep the order.
/// </summary>
public class OrderLegalEntityChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid OrderId, Guid EntityA, Guid EntityB, Guid EntityC);

    /// <summary>Customer default = A, allowed = {A, B}; C exists but is outside the allowed set.</summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var entityC = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "A", IsActive = true, IsDefault = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "B", IsActive = true },
            new LegalEntity { Id = entityC, TenantId = tenantId, LegalName = "C", IsActive = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true, DefaultLegalEntityId = entityA,
        });
        db.Context.CustomerAllowedLegalEntities.AddRange(
            new CustomerAllowedLegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, LegalEntityId = entityA },
            new CustomerAllowedLegalEntity { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, LegalEntityId = entityB });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.Completed, LegalEntityId = entityA,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var engine = new PricingEngine(db.Context, tenant);
        var permissions = new PermissionSet();
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissions);
        return new Harness(db, sut, permissions, tenantId, customerId, orderId, entityA, entityB, entityC);
    }

    private static async Task<Guid> AddInvoiceWithLineAsync(Harness h, InvoiceStatus status, Guid entityId)
    {
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.CustomerId, InvoiceNumber = $"FAC-{status}",
            InvoiceDate = new DateOnly(2026, 8, 20), DueDate = new DateOnly(2026, 9, 20), Status = status, LegalEntityId = entityId,
        });
        h.Db.Context.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = h.OrderId,
            Sequence = 0, Description = "Transport", Quantity = 1m, UnitPrice = 100m, VatRatePercent = 21m,
        });
        await h.Db.Context.SaveChangesAsync();
        return invoiceId;
    }

    [Fact]
    public async Task DeviatingFromTheCustomerDefault_NeedsTheOverrideRight_AndAReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Without the right: refused, and the preview says why.
        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.OrderId, h.EntityB, CancellationToken.None);
        Assert.True(impact!.DeviatesFromCustomerDefault);
        Assert.True(impact.RequiresOverridePermission);
        Assert.Null(impact.BlockedReason);

        var refused = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "reden"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains("rechten", refused.Error);

        // With the right but no reason: still refused.
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var noReason = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noReason.Outcome);
        Assert.Contains("reden", noReason.Error);

        // Right + reason: applied and audited with the reason.
        var ok = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "Klant factureert via B"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, ok.Outcome);
        Assert.Equal(h.EntityB, ok.Order!.LegalEntityId);

        var audit = await h.Db.Context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == h.OrderId.ToString() && a.Action == "LegalEntityChanged");
        Assert.Contains("Klant factureert via B", audit.NewValuesJson);
    }

    [Fact]
    public async Task AnEntityOutsideTheCustomersAllowedSet_IsRefused_EvenWithTheOverrideRight()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.OrderId, h.EntityC, CancellationToken.None);
        Assert.Contains("niet toegestaan", impact!.BlockedReason);

        var result = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityC, "x"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(h.EntityA, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == h.OrderId)).LegalEntityId);
    }

    [Fact]
    public async Task AnOrderOnASentInvoice_CannotChangeEntity_TheInvoiceStaysUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var invoiceId = await AddInvoiceWithLineAsync(h, InvoiceStatus.Sent, h.EntityA);

        var result = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        Assert.Contains("verzonden", result.Error);
        var invoice = await h.Db.Context.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(h.EntityA, invoice.LegalEntityId);
        Assert.Single(invoice.Lines.Where(l => !l.IsDeleted));
    }

    [Fact]
    public async Task DraftInvoiceLinesOfTheOldEntity_AreReleased_SoNoConceptStaysIncoherent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var draftId = await AddInvoiceWithLineAsync(h, InvoiceStatus.Draft, h.EntityA);
        // Real state: an order on a concept invoice already carries Status = Invoiced. Before the
        // audit fix this was refused as "gefactureerd", making the draft-release path unreachable.
        var invoiced = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == h.OrderId);
        invoiced.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.OrderId, h.EntityB, CancellationToken.None);
        Assert.Null(impact!.BlockedReason);
        Assert.Equal(1, impact.DraftInvoiceLinesReleased);

        var result = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "x"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);

        var remaining = await h.Db.Context.InvoiceLines.AsNoTracking()
            .CountAsync(l => l.InvoiceId == draftId && !l.IsDeleted);
        Assert.Equal(0, remaining);
        var released = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == h.OrderId);
        Assert.Equal(TransportOrderStatus.Completed, released.Status);
        Assert.Equal(h.EntityB, released.LegalEntityId);
    }

    /// <summary>
    /// Wave 1 fix A (A6) — releasing the order's draft invoice lines hands the ORDER back to
    /// Completed, but used to leave its pricing snapshot on Invoiced. `PricingStatusTransitions`
    /// has no way out of Invoiced and every pricing guard refuses it, so the order could only ever
    /// be re-invoiced at the stale price of the entity it just left. The snapshot now follows the
    /// order, through the same rule the invoice side uses when it releases an order.
    /// </summary>
    [Fact]
    public async Task ChangingEntity_OfADraftInvoicedOrder_ReleasesThePricingSnapshotToLocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        h.Permissions.Codes.Add(PermissionCodes.OrdersLockPrice);
        await AddInvoiceWithLineAsync(h, InvoiceStatus.Draft, h.EntityA);
        var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == h.OrderId);
        order.Status = TransportOrderStatus.Invoiced;
        var snapshotId = Guid.NewGuid();
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = snapshotId, TenantId = h.TenantId, TransportOrderId = h.OrderId,
            TariffDate = new DateOnly(2026, 8, 10), Currency = "EUR", Status = OrderPricingStatus.Invoiced,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.ChangeLegalEntityAsync(
            h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots.AsNoTracking()
            .SingleAsync(s => s.Id == snapshotId);
        Assert.Equal(OrderPricingStatus.Locked, snapshot.Status);

        // Locked is a state the price can be brought out of; Invoiced was a dead end.
        var unlocked = await h.Sut.SetOrderPricingStatusAsync(
            h.OrderId, OrderPricingStatus.Reviewed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, unlocked.Outcome);
    }

    /// <summary>
    /// Re-review N-2 — the DOSSIER twin of the entity change had no test at all, before or after
    /// this wave, while it carries the same A6 release and shares A7's predicate through
    /// <c>LoadLegalEntityChangeAsync</c>. It is also the path a user actually reaches from the
    /// dossier screen. Asserted end to end: the concept lines are released, the order is handed
    /// back to Completed, its price leaves the terminal Invoiced status, and the move is audited
    /// as a dossier action.
    /// </summary>
    [Fact]
    public async Task ChangingEntityViaTheDossier_OfADraftInvoicedOrder_ReleasesTheLinesOrderAndPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var draftId = await AddInvoiceWithLineAsync(h, InvoiceStatus.Draft, h.EntityA);
        var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == h.OrderId);
        order.Status = TransportOrderStatus.Invoiced;
        var snapshotId = Guid.NewGuid();
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = snapshotId, TenantId = h.TenantId, TransportOrderId = h.OrderId,
            TariffDate = new DateOnly(2026, 8, 10), Currency = "EUR", Status = OrderPricingStatus.Invoiced,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var error = await h.Sut.ChangeLegalEntityWithinDossierAsync(
            h.OrderId, h.EntityB, "Klant factureert via B", CancellationToken.None);

        Assert.Null(error);
        h.Db.Context.ChangeTracker.Clear();
        var moved = await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == h.OrderId);
        Assert.Equal(h.EntityB, moved.LegalEntityId);
        Assert.Equal(TransportOrderStatus.Completed, moved.Status);
        Assert.Equal(OrderPricingStatus.Locked,
            (await h.Db.Context.TransportOrderPricingSnapshots.AsNoTracking().SingleAsync(s => s.Id == snapshotId)).Status);
        Assert.Equal(0, await h.Db.Context.InvoiceLines.AsNoTracking()
            .CountAsync(l => l.InvoiceId == draftId && !l.IsDeleted));
        var audit = await h.Db.Context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == h.OrderId.ToString() && a.Action == "LegalEntityChanged");
        Assert.Contains("ViaDossier", audit.NewValuesJson);
    }

    /// <summary>The dossier path shares A7's predicate: a PAID invoice still blocks it.</summary>
    [Fact]
    public async Task ChangingEntityViaTheDossier_OfAnOrderOnAPaidInvoice_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddInvoiceWithLineAsync(h, InvoiceStatus.Paid, h.EntityA);

        var error = await h.Sut.ChangeLegalEntityWithinDossierAsync(
            h.OrderId, h.EntityB, "x", CancellationToken.None);

        Assert.NotNull(error);
        Assert.Contains("creditnota", error);
        h.Db.Context.ChangeTracker.Clear();
        Assert.Equal(h.EntityA,
            (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == h.OrderId)).LegalEntityId);
    }

    /// <summary>
    /// Wave 1 fix A (A7) — the guard blocked on "not Draft", which includes Cancelled. A cancelled
    /// draft is not an invoice: it was never sent, it cannot be credited (crediting needs
    /// Sent/Paid), so the user was told to "corrigeer via een creditnota" for a document that can
    /// never have one. Only Sent and Paid are finalized.
    /// </summary>
    [Fact]
    public async Task AnOrderOnACancelledDraftInvoice_CanStillChangeEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        await AddInvoiceWithLineAsync(h, InvoiceStatus.Cancelled, h.EntityA);

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.OrderId, h.EntityB, CancellationToken.None);
        Assert.Null(impact!.BlockedReason);

        var result = await h.Sut.ChangeLegalEntityAsync(
            h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        Assert.Equal(h.EntityB,
            (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == h.OrderId)).LegalEntityId);
    }

    /// <summary>A PAID invoice is finalized just like a sent one: the entity stays put.</summary>
    [Fact]
    public async Task AnOrderOnAPaidInvoice_CannotChangeEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        await AddInvoiceWithLineAsync(h, InvoiceStatus.Paid, h.EntityA);

        var result = await h.Sut.ChangeLegalEntityAsync(
            h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityB, "x"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        Assert.Contains("creditnota", result.Error!);
    }

    [Fact]
    public async Task MovingBackToTheCustomerDefault_NeedsNoOverrideRight()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == h.OrderId);
        order.LegalEntityId = h.EntityB;
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.OrderId, h.EntityA, CancellationToken.None);
        Assert.False(impact!.DeviatesFromCustomerDefault);
        Assert.False(impact.RequiresOverridePermission);

        var result = await h.Sut.ChangeLegalEntityAsync(h.OrderId, new ChangeOrderLegalEntityRequest(h.EntityA), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(h.EntityA, result.Order!.LegalEntityId);
    }
}
