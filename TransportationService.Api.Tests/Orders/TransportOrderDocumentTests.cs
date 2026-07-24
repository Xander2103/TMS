using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

public class TransportOrderDocumentTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"order-docs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderDocumentService Sut, Guid TenantId, Guid OrderId);

    private async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new DateOnly(2026, 7, 24),
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var storage = new LocalFileStorageService(_storageRoot);
        var sut = new TransportOrderDocumentService(db.Context, tenant, audit, storage);
        return new Harness(db, sut, tenantId, orderId);
    }

    [Fact]
    public async Task Crud_AndFileAttach_Work()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(h.OrderId, new SaveTransportOrderDocumentRequest(
            TransportOrderDocumentType.CustomerDeliveryNote, null, "Leverbon klant", new DateOnly(2026, 7, 24), null),
            CancellationToken.None);
        Assert.NotNull(created);
        Assert.False(created!.HasAttachment);

        using var upload = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        var attached = await h.Sut.AttachFileAsync(created.Id, "leverbon.pdf", "application/pdf", upload, CancellationToken.None);
        Assert.True(attached!.HasAttachment);
        Assert.Equal("leverbon.pdf", attached.FileName);

        var opened = await h.Sut.OpenFileAsync(created.Id, CancellationToken.None);
        Assert.NotNull(opened);
        using (var reader = new StreamReader(opened!.Value.Content))
        {
            Assert.Equal("pdf-bytes", await reader.ReadToEndAsync());
        }

        Assert.True(await h.Sut.DeleteAsync(created.Id, CancellationToken.None));
        Assert.Empty((await h.Sut.ListAsync(h.OrderId, CancellationToken.None))!);
    }

    [Fact]
    public async Task TenantIsolation_AndUnknownOrder_ReturnNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Null(await h.Sut.ListAsync(Guid.NewGuid(), CancellationToken.None));

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var otherSut = new TransportOrderDocumentService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)),
            new LocalFileStorageService(_storageRoot));
        Assert.Null(await otherSut.ListAsync(h.OrderId, CancellationToken.None));
    }

    [Fact]
    public async Task Create_RequiresTitle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(h.OrderId, new SaveTransportOrderDocumentRequest(
                TransportOrderDocumentType.Other, null, "  ", null, null), CancellationToken.None));
    }
}
