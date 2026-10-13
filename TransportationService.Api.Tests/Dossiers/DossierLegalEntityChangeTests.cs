using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Rule H (audit fix): the dossier is the commercial authority for its linked orders, so an
/// entity change on the dossier moves the orders that shared its entity in one unit of work,
/// releases their concept-invoice lines, and is refused as a whole when one order already sits
/// on a sent invoice — the dossier and its orders never drift apart silently.
/// </summary>
public class DossierLegalEntityChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, DossierService Sut, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid DossierId, Guid EntityA, Guid EntityB);

    /// <summary>Customer default = A; the dossier and its orders start on A.</summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "A", IsActive = true, IsDefault = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "B", IsActive = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true, DefaultLegalEntityId = entityA,
        });
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "D-1", Title = "Dossier", CustomerId = customerId,
            LegalEntityId = entityA, Status = DossierStatus.Open,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var permissions = new PermissionSet();
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            new PricingEngine(db.Context, tenant), currentUser, permissions);
        var sut = new DossierService(db.Context, tenant, audit, new TestClock(Now), null, permissions, currentUser, orders);
        return new Harness(db, sut, permissions, tenantId, customerId, dossierId, entityA, entityB);
    }

    private static async Task<Guid> AddLinkedOrderAsync(Harness h, string number, Guid entityId)
    {
        var orderId = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = h.TenantId, CustomerId = h.CustomerId, OrderNumber = number,
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.Completed, LegalEntityId = entityId,
        });
        h.Db.Context.DossierOrders.Add(new DossierOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = h.DossierId, TransportOrderId = orderId,
        });
        await h.Db.Context.SaveChangesAsync();
        return orderId;
    }

    private static async Task<Guid> AddInvoiceWithLineAsync(Harness h, Guid orderId, InvoiceStatus status, Guid entityId)
    {
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.CustomerId, InvoiceNumber = $"FAC-{status}-{orderId:N}"[..20],
            InvoiceDate = new DateOnly(2026, 8, 20), DueDate = new DateOnly(2026, 9, 20), Status = status, LegalEntityId = entityId,
        });
        h.Db.Context.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = orderId,
            Sequence = 0, Description = "Transport", Quantity = 1m, UnitPrice = 100m, VatRatePercent = 21m,
        });
        var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == orderId);
        order.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();
        return invoiceId;
    }

    [Fact]
    public async Task ChangingTheDossierEntity_MovesTheOrdersThatSharedIt_AndReleasesTheirConceptLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var o1 = await AddLinkedOrderAsync(h, "ORD-1", h.EntityA);
        var o2 = await AddLinkedOrderAsync(h, "ORD-2", h.EntityA);
        // An order deliberately put on B already is left alone.
        var elsewhere = await AddLinkedOrderAsync(h, "ORD-3", h.EntityB);
        var draftId = await AddInvoiceWithLineAsync(h, o1, InvoiceStatus.Draft, h.EntityA);

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.DossierId, h.EntityB, CancellationToken.None);
        Assert.Null(impact!.BlockedReason);
        Assert.Equal(["ORD-1", "ORD-2"], impact.Orders.Select(o => o.OrderNumber).ToArray());
        Assert.Equal(1, impact.DraftInvoiceLinesReleased);
        Assert.True(impact.DeviatesFromCustomerDefault);

        var result = await h.Sut.ChangeLegalEntityAsync(
            h.DossierId, new ChangeDossierEntityRequest(h.EntityB, null, "Andere entiteit factureert"), CancellationToken.None);

        Assert.Equal(h.EntityB, result!.LegalEntityId);
        var orders = await h.Db.Context.TransportOrders.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Assert.Equal(h.EntityB, orders[o1].LegalEntityId);
        Assert.Equal(h.EntityB, orders[o2].LegalEntityId);
        Assert.Equal(h.EntityB, orders[elsewhere].LegalEntityId);
        // The released order is invoiceable again; the concept lost its line but survives.
        Assert.Equal(TransportOrderStatus.Completed, orders[o1].Status);
        Assert.Equal(0, await h.Db.Context.InvoiceLines.AsNoTracking().CountAsync(l => l.InvoiceId == draftId && !l.IsDeleted));
        Assert.True(await h.Db.Context.Invoices.AsNoTracking().AnyAsync(i => i.Id == draftId));
    }

    [Fact]
    public async Task OneOrderOnASentInvoice_BlocksTheWholeDossierChange_NothingMoves()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var o1 = await AddLinkedOrderAsync(h, "ORD-1", h.EntityA);
        var o2 = await AddLinkedOrderAsync(h, "ORD-2", h.EntityA);
        await AddInvoiceWithLineAsync(h, o2, InvoiceStatus.Sent, h.EntityA);

        var impact = await h.Sut.PreviewLegalEntityChangeAsync(h.DossierId, h.EntityB, CancellationToken.None);
        Assert.Contains("ORD-2", impact!.BlockedReason);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.ChangeLegalEntityAsync(
            h.DossierId, new ChangeDossierEntityRequest(h.EntityB, null, "x"), CancellationToken.None));

        var dossier = await h.Db.Context.TransportDossiers.AsNoTracking().FirstAsync(d => d.Id == h.DossierId);
        Assert.Equal(h.EntityA, dossier.LegalEntityId);
        var first = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == o1);
        Assert.Equal(h.EntityA, first.LegalEntityId);
    }

    [Fact]
    public async Task DeviatingFromTheCustomerDefault_StillNeedsTheOverrideRight_ForTheWholeDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddLinkedOrderAsync(h, "ORD-1", h.EntityA);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.ChangeLegalEntityAsync(
            h.DossierId, new ChangeDossierEntityRequest(h.EntityB, null, "x"), CancellationToken.None));
    }
}
