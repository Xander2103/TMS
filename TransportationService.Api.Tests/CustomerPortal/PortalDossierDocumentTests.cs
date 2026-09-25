using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// Master sprint 2026-09-21 D6 — documents of a dossier AS A WHOLE in the customer portal. The level
/// a document hangs on never implies visibility: it is listed and downloadable only when it was
/// published (CustomerVisible), has a file, and the DOSSIER belongs to the portal user's customer —
/// and the download re-checks exactly that (the id alone is never trusted).
/// </summary>
public class PortalDossierDocumentTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid OwnUserId, Guid OtherUserId,
        Guid VisibleId, Guid InternalId, Guid NoFileId, Guid OtherCustomersId, LocalFileStorageService FileStorage)
    {
        public PortalDocumentService Portal(Guid userId, Guid? tenantId = null) =>
            new(Db.Context, new DevTenantContext(tenantId ?? TenantId), new DevCurrentUserContext(userId), FileStorage);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var ownUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var otherDossierId = Guid.NewGuid();
        var fileStorage = new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-portal-dossier-doc-tests", Guid.NewGuid().ToString("N")));

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = ownUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true },
            new User { Id = otherUserId, TenantId = tenantId, Email = "klant@andere.be", FirstName = "Anna", LastName = "Ander", CustomerId = otherCustomerId, IsActive = true });
        db.Context.TransportDossiers.AddRange(
            new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0001", Title = "Haven", CustomerId = customerId },
            new TransportDossier { Id = otherDossierId, TenantId = tenantId, DossierNumber = "DOS-0002", Title = "Andere", CustomerId = otherCustomerId });

        async Task<string> SaveFile(string name, string content)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            return await fileStorage.SaveAsync(tenantId, "order-documents", name, stream, CancellationToken.None);
        }

        TransportOrderDocument Document(Guid forDossier, string title, string? path, bool visible) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = null, DossierId = forDossier,
            DocumentType = TransportOrderDocumentType.Other, Title = title, DocumentPath = path,
            FileName = path is null ? null : $"{title}.pdf", ContentType = path is null ? null : "application/pdf",
            CustomerVisible = visible,
        };

        var visible = Document(dossierId, "Planning", await SaveFile("planning.pdf", "planning-content"), visible: true);
        var internalOnly = Document(dossierId, "Interne calculatie", await SaveFile("calculatie.pdf", "internal-content"), visible: false);
        var noFile = Document(dossierId, "Nog zonder bestand", null, visible: true);
        var otherCustomers = Document(otherDossierId, "Van een andere klant", await SaveFile("andere.pdf", "other-content"), visible: true);
        db.Context.TransportOrderDocuments.AddRange(visible, internalOnly, noFile, otherCustomers);
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantId, ownUserId, otherUserId, visible.Id, internalOnly.Id, noFile.Id, otherCustomers.Id, fileStorage);
    }

    [Fact]
    public async Task List_ShowsOnlyThePublishedDossierDocumentWithAFile_OfTheOwnCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var mine = (await h.Portal(h.OwnUserId).ListMyDocumentsAsync(CancellationToken.None)).Value!;
        var theirs = (await h.Portal(h.OtherUserId).ListMyDocumentsAsync(CancellationToken.None)).Value!;

        var listed = Assert.Single(mine);
        Assert.Equal((h.VisibleId, PortalDocumentSource.OrderDocument, "DOS-0001", null), (listed.Id, listed.Source, listed.DossierNumber, listed.OrderId));
        Assert.Equal(h.OtherCustomersId, Assert.Single(theirs).Id);
    }

    [Fact]
    public async Task Download_RechecksVisibilityFileAndCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var portal = h.Portal(h.OwnUserId);

        var ok = await portal.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.VisibleId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, ok.Outcome);
        Assert.Equal("planning-content", System.Text.Encoding.UTF8.GetString(ok.Value!.Content));

        // Internal, file-less and another customer's documents are all simply "not found" — a
        // guessed id never reveals that the document exists.
        foreach (var id in new[] { h.InternalId, h.NoFileId, h.OtherCustomersId, Guid.NewGuid() })
        {
            var refused = await portal.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, id, CancellationToken.None);
            Assert.Equal(PortalOutcomeKind.NotFound, refused.Outcome);
        }

        // The other customer's portal user cannot reach ours either — list AND download.
        var other = h.Portal(h.OtherUserId);
        Assert.DoesNotContain((await other.ListMyDocumentsAsync(CancellationToken.None)).Value!, d => d.Id == h.VisibleId);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await other.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.VisibleId, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WithdrawingPublication_RemovesItFromListAndDownload()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var document = h.Db.Context.TransportOrderDocuments.Single(d => d.Id == h.VisibleId);
        document.CustomerVisible = false;
        await h.Db.Context.SaveChangesAsync();

        Assert.Empty((await h.Portal(h.OwnUserId).ListMyDocumentsAsync(CancellationToken.None)).Value!);
        Assert.Equal(PortalOutcomeKind.NotFound,
            (await h.Portal(h.OwnUserId).GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.VisibleId, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task AnotherTenant_NeverReachesADossierDocument()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // The same user id presented under ANOTHER tenant resolves no customer link at all.
        var foreign = h.Portal(h.OwnUserId, tenantId: Guid.NewGuid());
        Assert.NotEqual(PortalOutcomeKind.Success, (await foreign.ListMyDocumentsAsync(CancellationToken.None)).Outcome);
        Assert.NotEqual(PortalOutcomeKind.Success,
            (await foreign.GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.VisibleId, CancellationToken.None)).Outcome);
    }
}
