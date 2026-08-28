using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Sprint 6 (audit completion) — the dossier is the commercial authority for its orders. Changing
/// its customer moves every linked order through the SAME per-order logic, in one transaction.
/// </summary>
public class DossierCustomerChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, DossierCustomerChangeService Sut, DossierService Dossiers,
        OrderCustomerChangeService Orders,
        Guid TenantId, Guid PlaceholderId, Guid RealCustomerId, Guid DossierId, Guid EntityA, Guid EntityB);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var placeholder = Guid.NewGuid();
        var real = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var dossierId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "A", IsActive = true, IsDefault = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "B", IsActive = true });
        db.Context.Customers.AddRange(
            new Customer { Id = placeholder, TenantId = tenantId, CustomerNumber = "TMP", Name = "VCB tijdelijk", IsActive = true, DefaultLegalEntityId = entityA },
            new Customer { Id = real, TenantId = tenantId, CustomerNumber = "KL-9", Name = "Client SA", IsActive = true, DefaultLegalEntityId = entityB, InvoiceLanguageCode = "fr", VatTreatment = VatTreatment.ReverseCharge });
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "D-1", Title = "Dossier", CustomerId = placeholder,
            LegalEntityId = entityA, Status = DossierStatus.Open,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new OrderCustomerChangeService(db.Context, tenant, audit);
        var dossiers = new DossierService(db.Context, tenant, audit, new TestClock(Now));
        var sut = new DossierCustomerChangeService(db.Context, tenant, audit, orders, dossiers);
        return new Harness(db, sut, dossiers, orders, tenantId, placeholder, real, dossierId, entityA, entityB);
    }

    private static async Task<Guid> AddLinkedOrderAsync(Harness h, string number, Guid customerId, bool withAutoPrice = true)
    {
        var orderId = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = h.TenantId, CustomerId = customerId, OrderNumber = number,
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.Completed, AgreedPrice = 300m,
            LegalEntityId = h.EntityA,
        });
        h.Db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = h.DossierId, TransportOrderId = orderId });
        h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Unloading,
            LocationName = "Magazijn", Address = "Noorderlaan 10", City = "Antwerpen", SnapshotAt = Now.UtcDateTime,
        });
        if (withAutoPrice)
        {
            h.Db.Context.TransportOrderPricingLines.Add(new TransportOrderPricingLine
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId, Sequence = 0,
                Label = "Transport", Amount = 300m, Source = "Regel", RuleName = "Tarief A", Kind = OrderPriceLineKind.Auto,
            });
        }

        await h.Db.Context.SaveChangesAsync();
        return orderId;
    }

    [Fact]
    public async Task ChangingTheDossierCustomer_MovesEveryLinkedOrder_InOneUnitOfWork()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var o1 = await AddLinkedOrderAsync(h, "ORD-1", h.PlaceholderId);
        var o2 = await AddLinkedOrderAsync(h, "ORD-2", h.PlaceholderId);

        var impact = await h.Sut.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(h.RealCustomerId, "Echte klant bekend"), CancellationToken.None);

        Assert.Equal(2, impact!.Orders.Count);
        var dossier = await h.Db.Context.TransportDossiers.AsNoTracking().FirstAsync(d => d.Id == h.DossierId);
        Assert.Equal(h.RealCustomerId, dossier.CustomerId);
        Assert.Equal(h.EntityB, dossier.LegalEntityId);
        foreach (var id in new[] { o1, o2 })
        {
            var order = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == id);
            Assert.Equal(h.RealCustomerId, order.CustomerId);
            Assert.Equal(h.EntityB, order.LegalEntityId);
            Assert.Null(order.AgreedPrice);
            // The old customer's automatic price is gone on every order; the stop snapshot stays.
            Assert.Empty(await h.Db.Context.TransportOrderPricingLines.AsNoTracking().Where(l => l.TransportOrderId == id).ToListAsync());
            Assert.Equal("Noorderlaan 10", (await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.TransportOrderId == id)).Address);
        }
    }

    [Fact]
    public async Task OneOrderOnASentInvoice_BlocksTheWholeDossier_AndNothingMoves()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var o1 = await AddLinkedOrderAsync(h, "ORD-1", h.PlaceholderId);
        var o2 = await AddLinkedOrderAsync(h, "ORD-2", h.PlaceholderId);
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.PlaceholderId, InvoiceNumber = "FAC-1",
            InvoiceDate = new DateOnly(2026, 8, 20), DueDate = new DateOnly(2026, 9, 20), Status = InvoiceStatus.Sent, LegalEntityId = h.EntityA,
        });
        h.Db.Context.Set<InvoiceLine>().Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = o2,
            Sequence = 0, Description = "x", Quantity = 1m, UnitPrice = 1m, VatRatePercent = 21m,
        });
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Sut.PreviewAsync(h.DossierId, h.RealCustomerId, CancellationToken.None);
        Assert.Contains("ORD-2", impact!.BlockedReason);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(h.RealCustomerId, "x"), CancellationToken.None));

        // Atomic: the unblocked order did not move either.
        Assert.Equal(h.PlaceholderId, (await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == o1)).CustomerId);
        Assert.Equal(h.PlaceholderId, (await h.Db.Context.TransportDossiers.AsNoTracking().FirstAsync(d => d.Id == h.DossierId)).CustomerId);
    }

    [Fact]
    public async Task AnOrderAlreadyOnAnotherCustomer_IsLeftAlone_AndReported()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var other = new Customer { Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerNumber = "KL-3", Name = "Derde", IsActive = true };
        h.Db.Context.Customers.Add(other);
        await h.Db.Context.SaveChangesAsync();
        var mine = await AddLinkedOrderAsync(h, "ORD-1", h.PlaceholderId);
        var theirs = await AddLinkedOrderAsync(h, "ORD-2", other.Id);

        var impact = await h.Sut.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(h.RealCustomerId, "x"), CancellationToken.None);

        Assert.Equal(["ORD-2"], impact!.OrdersLeftOnOtherCustomer);
        Assert.Equal(h.RealCustomerId, (await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == mine)).CustomerId);
        Assert.Equal(other.Id, (await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == theirs)).CustomerId);
    }

    [Fact]
    public async Task ThePlainDossierUpdate_CanNoLongerSwapTheCustomerSilently()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddLinkedOrderAsync(h, "ORD-1", h.PlaceholderId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Dossiers.UpdateAsync(h.DossierId, new SaveDossierRequest(CustomerId: h.RealCustomerId), CancellationToken.None));
        Assert.Contains("Klant wijzigen", ex.Message);

        Assert.Equal(h.PlaceholderId, (await h.Db.Context.TransportDossiers.AsNoTracking().FirstAsync(d => d.Id == h.DossierId)).CustomerId);
    }

    [Fact]
    public async Task AnOrderInsideADossier_MustBeChangedOnTheDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orderId = await AddLinkedOrderAsync(h, "ORD-1", h.PlaceholderId);

        var impact = await h.Orders.PreviewAsync(orderId, h.RealCustomerId, CancellationToken.None);

        Assert.Equal(h.DossierId, impact!.OwningDossierId);
        Assert.Contains("D-1", impact.BlockedReason);
    }
}
