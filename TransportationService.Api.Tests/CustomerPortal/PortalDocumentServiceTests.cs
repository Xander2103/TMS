using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class PortalDocumentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid OtherCustomerId,
        Guid OrderId, Guid PortalUserId, PortalDocumentService Sut, LocalFileStorageService FileStorage);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var foreignOrderId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-portal-doc-tests", Guid.NewGuid().ToString("N"));
        var fileStorage = new LocalFileStorageService(storageRoot);

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.Add(new User
        {
            Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant",
            CustomerId = customerId, IsActive = true,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Completed,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = foreignOrderId, TenantId = tenantId, CustomerId = otherCustomerId, OrderNumber = "ORD-2",
            OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Completed,
        });

        async Task<string> SaveFile(string category, string name, string content)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            return await fileStorage.SaveAsync(tenantId, category, name, stream, CancellationToken.None);
        }

        var orderDocPath = await SaveFile("order-documents", "cmr.pdf", "cmr-content");
        db.Context.TransportOrderDocuments.Add(new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
            DocumentType = TransportOrderDocumentType.Cmr, Title = "CMR", DocumentPath = orderDocPath,
            FileName = "cmr.pdf", ContentType = "application/pdf", CustomerVisible = true,
        });
        // H-14: an internal attachment on the SAME order — not marked customer-visible, so the
        // portal must neither list it nor serve its bytes.
        var internalDocPath = await SaveFile("order-documents", "intern.pdf", "internal-content");
        db.Context.TransportOrderDocuments.Add(new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
            DocumentType = TransportOrderDocumentType.Other, Title = "Interne schadefoto", DocumentPath = internalDocPath,
            FileName = "intern.pdf", ContentType = "application/pdf",
        });

        // ProofOfDelivery has real FKs to Trip and TransportOrderStop — both must exist.
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var hiddenStopId = Guid.NewGuid();
        db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "TR-1", TripDate = new DateOnly(2026, 7, 30),
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = stopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" },
            new TransportOrderStop { Id = hiddenStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 3, StopType = StopType.Unloading, City = "Gent" });

        var signaturePath = await SaveFile("pod-signatures", "signature.png", "signature-bytes");
        db.Context.ProofsOfDelivery.Add(new ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId,
            TransportOrderStopId = stopId, Version = 1, IsCurrent = true, CustomerVisible = true,
            RecipientName = "Jan Ontvanger", DeliveredAt = Now.UtcDateTime, SignaturePath = signaturePath,
        });
        // A hidden POD (CustomerVisible = false) must never surface in the portal.
        db.Context.ProofsOfDelivery.Add(new ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId,
            TransportOrderStopId = hiddenStopId, Version = 1, IsCurrent = true, CustomerVisible = false,
            RecipientName = "Verborgen", DeliveredAt = Now.UtcDateTime, SignaturePath = signaturePath,
        });

        var invoiceId = Guid.NewGuid();
        db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = tenantId, CustomerId = customerId, InvoiceNumber = "2026070001",
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 7, InvoiceDate = new DateOnly(2026, 7, 30),
            DueDate = new DateOnly(2026, 8, 29), Status = InvoiceStatus.Sent, Currency = "EUR",
        });
        var attachmentPath = await SaveFile("invoices", "factuur.pdf", "invoice-content");
        db.Context.InvoiceAttachments.Add(new InvoiceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceId, FileName = "factuur.pdf",
            ContentType = "application/pdf", SizeBytes = 20, StorageKey = attachmentPath, IncludeWhenSending = true,
        });
        // Internal-only attachment (IncludeWhenSending = false) must never surface either.
        db.Context.InvoiceAttachments.Add(new InvoiceAttachment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceId, FileName = "intern.xlsx",
            ContentType = "application/vnd.ms-excel", SizeBytes = 20, StorageKey = attachmentPath, IncludeWhenSending = false,
        });

        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new PortalDocumentService(db.Context, tenant, new DevCurrentUserContext(portalUserId), fileStorage);
        return new Harness(db, tenantId, customerId, otherCustomerId, orderId, portalUserId, sut, fileStorage);
    }

    [Fact]
    public async Task List_AggregatesAllThreeSources_ExcludingHiddenAndInternalOnes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ListMyDocumentsAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        var items = result.Value!;

        Assert.Single(items, d => d.Source == PortalDocumentSource.OrderDocument);
        Assert.Single(items, d => d.Source == PortalDocumentSource.Pod);
        Assert.Single(items, d => d.Source == PortalDocumentSource.InvoiceAttachment);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Content_OrderDocument_ReturnsBytes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var docId = (await h.Db.Context.TransportOrderDocuments.FirstAsync(d => d.CustomerVisible)).Id;
        var result = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, docId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        Assert.True(result.Value!.Content.Length > 0);
    }

    /// <summary>
    /// H-14: order documents used to be published to the portal wholesale — every CMR, damage
    /// photo and internal note attached to an order of this customer. They are now opt-in
    /// (CustomerVisible, default false) and the filter guards the list AND the content endpoint.
    /// </summary>
    [Fact]
    public async Task List_OmitsOrderDocumentsThatAreNotMarkedCustomerVisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ListMyDocumentsAsync(CancellationToken.None);

        var orderDoc = Assert.Single(result.Value!, d => d.Source == PortalDocumentSource.OrderDocument);
        Assert.Equal("CMR", orderDoc.Title);
        Assert.DoesNotContain(result.Value!, d => d.Title == "Interne schadefoto");
    }

    [Fact]
    public async Task Content_OrderDocumentNotMarkedCustomerVisible_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var internalId = (await h.Db.Context.TransportOrderDocuments.FirstAsync(d => !d.CustomerVisible)).Id;
        var result = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, internalId, CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task DeactivatedCustomer_SeesAndDownloadsNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var visibleId = (await h.Db.Context.TransportOrderDocuments.FirstAsync(d => d.CustomerVisible)).Id;

        var customer = await h.Db.Context.Customers.FirstAsync(c => c.Id == h.CustomerId);
        customer.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await h.Sut.ListMyDocumentsAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink,
            (await h.Sut.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, visibleId, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Content_HiddenPod_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var hiddenPodId = (await h.Db.Context.ProofsOfDelivery.FirstAsync(p => !p.CustomerVisible)).Id;
        var result = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.Pod, hiddenPodId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Content_InternalOnlyAttachment_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var internalId = (await h.Db.Context.InvoiceAttachments.FirstAsync(a => !a.IncludeWhenSending)).Id;
        var result = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.InvoiceAttachment, internalId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Content_ForeignCustomerOrderDocument_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // A different tenant/customer's user should never reach this order's document; simulate
        // by asking with a made-up id that doesn't belong to this customer's data set.
        var result = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    /// <summary>
    /// Fix round 1 (Important #1): PodService.CorrectAsync only flips the ORIGINAL row's
    /// IsCurrent to false — it never clears CustomerVisible — so a customer with a bookmarked
    /// download URL for the superseded version must still be refused. The list already filtered
    /// on IsCurrent; the content endpoint didn't and is now aligned with it.
    /// </summary>
    [Fact]
    public async Task Content_CorrectedPod_OldVersion_ReturnsNotFound_NewVersionStillDownloads()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var original = await h.Db.Context.ProofsOfDelivery.FirstAsync(p => p.CustomerVisible);
        var replacementPath = original.SignaturePath;

        // Simulate PodService.CorrectAsync: the original is superseded (IsCurrent = false,
        // CustomerVisible left untouched) and a new current version is inserted.
        original.IsCurrent = false;
        var replacement = new TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = original.TripId, TransportOrderId = original.TransportOrderId,
            TransportOrderStopId = original.TransportOrderStopId, Version = 2, IsCurrent = true, CustomerVisible = true,
            RecipientName = "Gecorrigeerd", DeliveredAt = original.DeliveredAt, SignaturePath = replacementPath,
            CorrectedFromPodId = original.Id, CorrectionReason = "Verkeerde ontvanger genoteerd",
        };
        h.Db.Context.ProofsOfDelivery.Add(replacement);
        await h.Db.Context.SaveChangesAsync();

        var oldResult = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.Pod, original.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, oldResult.Outcome);

        var newResult = await h.Sut.GetDocumentContentAsync(PortalDocumentSource.Pod, replacement.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, newResult.Outcome);
    }
}
