using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

/// <summary>
/// MD-H10 (master-data acceptance 2026-10-18): registering the customer's PO left the completed
/// order on "Nakijken: po.missing" until the price was unlocked and relocked through the API. The
/// PO registry (and the PO policy) now re-evaluate the customer's completed orders directly.
/// </summary>
public class PoRegistryReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisteringAValidPo_MakesTheCompletedOrderInvoiceReady_RemovingItRevertsIt()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "TMK-001", Name = "TEST MASTER Klant", IsActive = true,
            PurchaseOrderPolicy = PurchaseOrderPolicy.Required, PurchaseOrderRequired = true,
        });
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0101", OrderDate = new DateOnly(2026, 10, 17),
            Status = TransportOrderStatus.Completed, AgreedPrice = 480m, PriceIsManual = true,
        };
        db.Context.TransportOrders.Add(order);
        await db.Context.SaveChangesAsync();
        await InvoiceReadinessEvaluator.EvaluateAsync(db.Context, order, CancellationToken.None);
        await db.Context.SaveChangesAsync();
        Assert.Equal((InvoiceReadinessEvaluator.ReviewRequired, "po.missing"), (order.InvoiceReadiness, order.InvoiceReadinessReasons));

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now));

        var policy = await sut.AddPoNumberAsync(customerId, new SaveCustomerPoNumberRequest("PO-TMK-0002", new DateOnly(2026, 10, 1), null, null), CancellationToken.None);
        db.Context.ChangeTracker.Clear();
        var refreshed = await db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(InvoiceReadinessEvaluator.ReadyForInvoice, refreshed.InvoiceReadiness);
        Assert.Null(refreshed.InvoiceReadinessReasons);

        var poId = await db.Context.CustomerPurchaseOrderNumbers.Select(p => p.Id).SingleAsync();
        Assert.True(await sut.DeletePoNumberAsync(customerId, poId, CancellationToken.None));
        db.Context.ChangeTracker.Clear();
        Assert.Equal("po.missing", (await db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).InvoiceReadinessReasons);
        Assert.NotNull(policy);
    }
}
