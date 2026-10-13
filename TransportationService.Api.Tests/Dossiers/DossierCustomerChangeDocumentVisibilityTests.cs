using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Closure sprint 2026-09-23 (P0 security) — a document published for customer A must never
/// become visible to customer B just because the dossier (or an order) moved to B. The customer
/// change withdraws the publication (back to internal, file untouched, audited); an internal
/// user republishes it deliberately for the new customer.
/// </summary>
public class DossierCustomerChangeDocumentVisibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, DossierCustomerChangeService Sut, OrderCustomerChangeService Orders,
        Guid TenantId, Guid CustomerA, Guid CustomerB, Guid UserA, Guid UserB,
        Guid DossierId, Guid OrderId, Guid DossierDocId, Guid OrderDocId, Guid InternalDocId,
        LocalFileStorageService FileStorage)
    {
        public PortalDocumentService Portal(Guid userId) =>
            new(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(userId), FileStorage);

        public Task<TransportOrderDocument> Doc(Guid id) =>
            Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var fileStorage = new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-customer-change-doc-tests", Guid.NewGuid().ToString("N")));

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerA, TenantId = tenantId, CustomerNumber = "KL-A", Name = "Klant A", IsActive = true },
            new Customer { Id = customerB, TenantId = tenantId, CustomerNumber = "KL-B", Name = "Klant B", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = userA, TenantId = tenantId, Email = "a@a.be", FirstName = "A", LastName = "A", CustomerId = customerA, IsActive = true },
            new User { Id = userB, TenantId = tenantId, Email = "b@b.be", FirstName = "B", LastName = "B", CustomerId = customerB, IsActive = true });
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-A", Title = "Dossier", CustomerId = customerA, Status = DossierStatus.Open,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerA, OrderNumber = "ORD-A",
            OrderDate = new DateOnly(2026, 9, 10), Status = TransportOrderStatus.Completed,
        });
        db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId });

        async Task<string> SaveFile(string name)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(name));
            return await fileStorage.SaveAsync(tenantId, "order-documents", name, stream, CancellationToken.None);
        }

        var dossierDoc = new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = null, DossierId = dossierId,
            DocumentType = TransportOrderDocumentType.Other, Title = "Dossierplanning",
            DocumentPath = await SaveFile("dossier.pdf"), FileName = "dossier.pdf", ContentType = "application/pdf", CustomerVisible = true,
        };
        var orderDoc = new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, DossierId = dossierId,
            DocumentType = TransportOrderDocumentType.Other, Title = "Orderbon",
            DocumentPath = await SaveFile("order.pdf"), FileName = "order.pdf", ContentType = "application/pdf", CustomerVisible = true,
        };
        var internalDoc = new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = null, DossierId = dossierId,
            DocumentType = TransportOrderDocumentType.Other, Title = "Interne calculatie",
            DocumentPath = await SaveFile("intern.pdf"), FileName = "intern.pdf", ContentType = "application/pdf", CustomerVisible = false,
        };
        db.Context.TransportOrderDocuments.AddRange(dossierDoc, orderDoc, internalDoc);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var orders = new OrderCustomerChangeService(db.Context, tenant, audit);
        var dossiers = new DossierService(db.Context, tenant, audit, new TestClock(Now));
        var sut = new DossierCustomerChangeService(db.Context, tenant, audit, orders, dossiers);
        return new Harness(db, sut, orders, tenantId, customerA, customerB, userA, userB,
            dossierId, orderId, dossierDoc.Id, orderDoc.Id, internalDoc.Id, fileStorage);
    }

    private static Task<DossierCustomerChangeImpactDto?> MoveToB(Harness h) =>
        h.Sut.ApplyAsync(h.DossierId, new ChangeDossierCustomerRequest(h.CustomerB, "Echte klant bekend"), CancellationToken.None);

    [Fact]
    public async Task BeforeTheChange_CustomerASeesBothPublishedDocuments()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var ids = (await h.Portal(h.UserA).ListMyDocumentsAsync(CancellationToken.None)).Value!.Select(d => d.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { h.DossierDocId, h.OrderDocId }, ids);
    }

    [Fact]
    public async Task ChangingTheCustomer_WithdrawsPublication_OfDossierAndOrderDocuments()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var impact = await MoveToB(h);

        // The new customer sees NOTHING that was published for the old one — list and download.
        var portalB = h.Portal(h.UserB);
        Assert.Empty((await portalB.ListMyDocumentsAsync(CancellationToken.None)).Value!);
        foreach (var id in new[] { h.DossierDocId, h.OrderDocId })
        {
            Assert.Equal(PortalOutcomeKind.NotFound,
                (await portalB.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, id, CancellationToken.None)).Outcome);
        }

        // Both documents are internal again; the files themselves are untouched and still attached.
        var dossierDoc = await h.Doc(h.DossierDocId);
        var orderDoc = await h.Doc(h.OrderDocId);
        Assert.False(dossierDoc.CustomerVisible);
        Assert.False(orderDoc.CustomerVisible);
        Assert.NotNull(dossierDoc.DocumentPath);
        Assert.NotNull(orderDoc.DocumentPath);
        Assert.False(dossierDoc.IsDeleted);
        Assert.False(orderDoc.IsDeleted);
        Assert.Equal(h.DossierId, dossierDoc.DossierId);
        Assert.Equal(h.OrderId, orderDoc.TransportOrderId);

        // The impact reports what was withdrawn: one dossier document, one on the moved order.
        Assert.Equal(1, impact!.DocumentsPublicationWithdrawn);
        Assert.Equal(1, Assert.Single(impact.Orders).DocumentsPublicationWithdrawn);
    }

    [Fact]
    public async Task TheWithdrawal_IsAudited_PerDocument()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await MoveToB(h);

        var entries = await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "TransportOrderDocument" && a.Action == "CustomerVisibilityWithdrawn")
            .ToListAsync();
        Assert.Equal(new HashSet<string> { h.DossierDocId.ToString(), h.OrderDocId.ToString() }, entries.Select(e => e.EntityId).ToHashSet());
        Assert.All(entries, e => Assert.Contains("CustomerChanged", e.NewValuesJson));
        // The internal document was never published, so nothing is recorded for it.
        Assert.DoesNotContain(entries, e => e.EntityId == h.InternalDocId.ToString());
    }

    [Fact]
    public async Task AfterDeliberateRepublication_CustomerBSeesIt_AndCustomerANeverAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await MoveToB(h);

        // Before republication the OLD customer has lost the dossier entirely.
        Assert.Empty((await h.Portal(h.UserA).ListMyDocumentsAsync(CancellationToken.None)).Value!);

        // An internal user consciously publishes again for the new customer.
        foreach (var id in new[] { h.DossierDocId, h.OrderDocId })
        {
            var tracked = await h.Db.Context.TransportOrderDocuments.SingleAsync(d => d.Id == id);
            tracked.CustomerVisible = true;
        }
        await h.Db.Context.SaveChangesAsync();

        var idsB = (await h.Portal(h.UserB).ListMyDocumentsAsync(CancellationToken.None)).Value!.Select(d => d.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { h.DossierDocId, h.OrderDocId }, idsB);
        Assert.Equal(PortalOutcomeKind.Success,
            (await h.Portal(h.UserB).GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.DossierDocId, CancellationToken.None)).Outcome);

        // Customer A no longer reaches anything through the moved dossier — list or download.
        var portalA = h.Portal(h.UserA);
        Assert.Empty((await portalA.ListMyDocumentsAsync(CancellationToken.None)).Value!);
        foreach (var id in new[] { h.DossierDocId, h.OrderDocId })
        {
            Assert.Equal(PortalOutcomeKind.NotFound,
                (await portalA.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, id, CancellationToken.None)).Outcome);
        }
    }

    [Fact]
    public async Task AStandaloneOrderCustomerChange_WithdrawsItsOrderDocuments_Too()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Detach the order from the dossier so the per-order flow applies.
        h.Db.Context.DossierOrders.RemoveRange(h.Db.Context.DossierOrders.Where(l => l.TransportOrderId == h.OrderId));
        await h.Db.Context.SaveChangesAsync();

        var impact = await h.Orders.ApplyAsync(h.OrderId, new ChangeOrderCustomerRequest(h.CustomerB, "Echte klant"), CancellationToken.None);

        Assert.Equal(1, impact!.DocumentsPublicationWithdrawn);
        Assert.False((await h.Doc(h.OrderDocId)).CustomerVisible);
        // The dossier stayed on A: its own published document is untouched.
        Assert.True((await h.Doc(h.DossierDocId)).CustomerVisible);
        Assert.DoesNotContain((await h.Portal(h.UserB).ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == h.OrderDocId);
    }

    [Fact]
    public async Task ThePreview_AnnouncesTheWithdrawal_WithoutChangingAnything()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var impact = await h.Sut.PreviewAsync(h.DossierId, h.CustomerB, CancellationToken.None);

        Assert.Equal(1, impact!.DocumentsPublicationWithdrawn);
        Assert.Equal(1, Assert.Single(impact.Orders).DocumentsPublicationWithdrawn);
        Assert.True((await h.Doc(h.DossierDocId)).CustomerVisible);
        Assert.True((await h.Doc(h.OrderDocId)).CustomerVisible);
    }

    [Fact]
    public async Task AnotherTenantsPublishedDocuments_AreNeverTouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = Guid.NewGuid();
        var foreignDossier = Guid.NewGuid();
        var foreignOrder = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = foreignTenant, CustomerNumber = "X", Name = "X", IsActive = true });
        h.Db.Context.TransportDossiers.Add(new TransportDossier { Id = foreignDossier, TenantId = foreignTenant, DossierNumber = "DOS-A", Title = "Elders", CustomerId = h.CustomerA });
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = foreignOrder, TenantId = foreignTenant, CustomerId = h.CustomerA, OrderNumber = "ORD-A",
            OrderDate = new DateOnly(2026, 9, 10), Status = TransportOrderStatus.Completed,
        });
        var foreignDossierDoc = new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, TransportOrderId = null, DossierId = foreignDossier,
            DocumentType = TransportOrderDocumentType.Other, Title = "Elders", DocumentPath = "x/elders.pdf", FileName = "elders.pdf", CustomerVisible = true,
        };
        var foreignOrderDoc = new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, TransportOrderId = foreignOrder, DossierId = foreignDossier,
            DocumentType = TransportOrderDocumentType.Other, Title = "Elders order", DocumentPath = "x/elders-o.pdf", FileName = "elders-o.pdf", CustomerVisible = true,
        };
        h.Db.Context.TransportOrderDocuments.AddRange(foreignDossierDoc, foreignOrderDoc);
        await h.Db.Context.SaveChangesAsync();

        await MoveToB(h);

        Assert.True((await h.Doc(foreignDossierDoc.Id)).CustomerVisible);
        Assert.True((await h.Doc(foreignOrderDoc.Id)).CustomerVisible);
        Assert.False((await h.Doc(h.DossierDocId)).CustomerVisible);
    }
}
