using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// C-04 (wave 1 production blockers): <see cref="TransportOrderStatus.Invoiced"/> is set by the
/// invoicing module but had no entry in the service's transition map, while both
/// ChangeStatusAsync and MapDetailAsync indexed that map directly. Every invoiced order
/// therefore threw <see cref="KeyNotFoundException"/> — a hard 500 on GET
/// /api/transport-orders/{id}. The map must cover EVERY enum member and both call sites must
/// read it defensively.
/// </summary>
public class OrderInvoicedStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Terminal, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now));
        return new Harness(db, sut, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput Stop(StopType type, Guid? locationId = null, string? city = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-777", new DateOnly(2026, 8, 30), "20 paletten bouwmateriaal",
        20, "paletten", 12500, null, 20, false, false, 1450m, null, stops);

    private static async Task<Guid> CreateInvoicedOrderAsync(Harness h)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = created.Order!.Id;

        // The invoicing module sets Invoiced directly on the entity (it is not reachable through
        // the manual transition map) — reproduce exactly that.
        var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == id);
        order.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();
        return id;
    }

    /// <summary>
    /// Reflection guard: a new status member must never again be able to reach production
    /// without a transition entry.
    /// </summary>
    [Fact]
    public async Task Transitions_CoverEveryTransportOrderStatusMember()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        foreach (var status in Enum.GetValues<TransportOrderStatus>())
        {
            var created = await h.Sut.CreateAsync(Request(h.CustomerId,
                Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
            var order = await h.Db.Context.TransportOrders.FirstAsync(o => o.Id == created.Order!.Id);
            order.Status = status;
            await h.Db.Context.SaveChangesAsync();
            h.Db.Context.ChangeTracker.Clear();

            var detail = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
            Assert.NotNull(detail);
            Assert.NotNull(detail!.AllowedTransitions);
        }
    }

    [Fact]
    public async Task GetById_InvoicedOrder_Returns_Detail_WithEmptyTransitions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await CreateInvoicedOrderAsync(h);

        var detail = await h.Sut.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(TransportOrderStatus.Invoiced, detail!.Status);
        Assert.Empty(detail.AllowedTransitions);
        // Invoiced is deliberately absent from the CORRECTIVE map too: unwinding a financial
        // document runs through invoicing, never through a status rollback.
        Assert.Empty(detail.AllowedCorrections);
        Assert.False(detail.CanCancel);
    }

    [Fact]
    public async Task ChangeStatus_OnInvoicedOrder_ReturnsInvalidState_NotException()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await CreateInvoicedOrderAsync(h);

        var result = await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Completed, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        var persisted = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == id);
        Assert.Equal(TransportOrderStatus.Invoiced, persisted.Status);
    }
}
