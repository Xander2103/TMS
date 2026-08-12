using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Wave 9: delivery-note/CMR generation from frozen order data and the merged batch per trip
/// (one PDF, one page per order, route order). Unknown orders/trips yield null, never a crash.
/// </summary>
public class TransportDocumentTests
{
    private sealed record Harness(SqliteTestDbContext Db, TransportDocumentService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Street = "Kaai", HouseNumber = "12", PostalCode = "9000", City = "Gent", VatNumber = "BE0987654321",
            IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new TransportDocumentService(db.Context, new DevTenantContext(tenantId)), tenantId, customerId);
    }

    private static TransportOrder Order(Harness h, string number)
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            OrderNumber = number, OrderDate = new DateOnly(2026, 8, 12),
            GoodsDescription = "20 paletten machineonderdelen", Quantity = 20, WeightKg = 4800,
            CustomerReference = "REF-77",
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, StopType = StopType.Loading,
            City = "Antwerpen", PostalCode = "2000", CountryCode = "BE",
        });
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 2, StopType = StopType.Unloading,
            City = "Hasselt", PostalCode = "3500", CountryCode = "BE", Reference = "Poort 4",
        });
        h.Db.Context.TransportOrders.Add(order);
        return order;
    }

    private static int PageCount(byte[] pdfBytes)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(
            new MemoryStream(pdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Theory]
    [InlineData("delivery-note", "leveringsbon-ORD-1.pdf")]
    [InlineData("cmr", "cmr-ORD-1.pdf")]
    public async Task Render_ProducesAOnePagePdf_WithTheRightFileName(string kind, string expectedFileName)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Order(h, "ORD-1");
        await h.Db.Context.SaveChangesAsync();
        var orderId = h.Db.Context.TransportOrders.Single().Id;

        var result = await h.Sut.RenderAsync(orderId, kind, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedFileName, result!.Value.FileName);
        Assert.True(result.Value.Content.Length > 800);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Value.Content, 0, 5));
        Assert.Equal(1, PageCount(result.Value.Content));
    }

    [Fact]
    public async Task TripBatch_MergesOnePagePerOrder_InRouteOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order1 = Order(h, "ORD-1");
        var order2 = Order(h, "ORD-2");
        var trip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0001",
            TripDate = new DateOnly(2026, 8, 12), Status = TripStatus.Planned,
        };
        h.Db.Context.Trips.Add(trip);
        h.Db.Context.TripOrders.AddRange(
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = order2.Id, Sequence = 1 },
            new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = order1.Id, Sequence = 2 });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.RenderTripBatchAsync(trip.Id, "cmr", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("cmr-RIT-0001.pdf", result!.Value.FileName);
        Assert.Equal(2, PageCount(result.Value.Content));
    }

    [Fact]
    public async Task UnknownOrderOrTrip_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Null(await h.Sut.RenderAsync(Guid.NewGuid(), "cmr", CancellationToken.None));
        Assert.Null(await h.Sut.RenderTripBatchAsync(Guid.NewGuid(), "cmr", CancellationToken.None));
    }
}
