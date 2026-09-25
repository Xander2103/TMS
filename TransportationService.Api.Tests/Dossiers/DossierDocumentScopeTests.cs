using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Controllers;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Master sprint 2026-09-21 D6 (contract 4.2) — ONE document entity on two levels: a document hangs
/// on the dossier as a whole or on one of its orders, is listed and counted exactly once, moves by
/// changing its link only, and is guarded per scope (rights, closed dossier, tenant, other dossier).
/// </summary>
public class DossierDocumentScopeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"dossier-docs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TestClock Clock, Guid TenantId, Guid CustomerId, Guid UserId, PermissionSet Permissions, string StorageRoot)
    {
        private DevTenantContext Tenant => new(TenantId);

        private AuditService Audit(ITenantContext tenant, Guid? userId) => new(Db.Context, tenant, new DevCurrentUserContext(userId));

        public DossierService Dossiers() => new(Db.Context, Tenant, Audit(Tenant, null), Clock);

        public DossierActivityService Activities()
        {
            var tenant = Tenant;
            var orders = new TransportOrderService(Db.Context, tenant, Audit(tenant, null), Clock);
            return new DossierActivityService(Db.Context, tenant, Audit(tenant, null), Dossiers(), orders, Clock);
        }

        public TransportOrderDocumentService Documents(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new TransportOrderDocumentService(
                Db.Context, tenant, Audit(tenant, UserId), new LocalFileStorageService(StorageRoot), new DevCurrentUserContext(UserId));
        }

        public TransportOrderDocumentsController Controller()
        {
            var documents = Documents();
            return new TransportOrderDocumentsController(
                documents, new OrderDocumentAccessResolver(documents, new DevCurrentUserContext(UserId), Permissions));
        }

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        /// <summary>A dossier with a standalone activity plus <paramref name="orderCount"/> transport activities, each with its own order.</summary>
        public async Task<(DossierDetailDto Dossier, List<Guid> OrderIds)> DossierWithOrdersAsync(int orderCount)
        {
            var dossier = await Dossiers().CreateAsync(
                new SaveDossierRequest(CustomerId: CustomerId, ActivityTypeId: await TypeIdAsync("OPSLAG")), CancellationToken.None);
            var transport = await TypeIdAsync("DIRECT_TRANSPORT");
            for (var i = 0; i < orderCount; i++)
            {
                dossier = (await Activities().AddAsync(dossier.Id,
                    new SaveDossierActivityRequest(transport, $"Rit {i + 1}", CreateLinkedOrder: true), CancellationToken.None))!;
            }

            return (dossier, dossier.Activities!.Where(a => a.LinkedTransportOrderId is not null)
                .OrderBy(a => a.Sequence).Select(a => a.LinkedTransportOrderId!.Value).ToList());
        }
    }

    private async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = entityId, TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV",
            IsActive = true, DefaultLegalEntityId = entityId,
        });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "thalie@acme.test", FirstName = "Thalie", LastName = "Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, new TestClock(Now), tenantId, customerId, userId, new PermissionSet(), _storageRoot);
    }

    private static CreateDossierDocumentRequest General(string title, Guid? orderId = null, bool? visible = null) =>
        new(orderId, TransportOrderDocumentType.Other, null, title, null, null, visible);

    private static async Task AttachAsync(TransportOrderDocumentService documents, Guid id, DossierDocumentScope scope, string fileName = "plan.pdf")
    {
        using var upload = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        Assert.NotNull(await documents.AttachFileAsync(id, fileName, "application/pdf", upload, CancellationToken.None, scope));
    }

    // ---- one document, listed and counted once ----

    [Fact]
    public async Task GeneralDocument_IsListedOnce_InNoOrdersOwnList_AndCountedOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(2);

        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Veiligheidsplan"), CancellationToken.None))!;

        Assert.Equal((DossierDocumentScope.Dossier, dossier.Id, null, null), (general.Scope, general.DossierId, general.TransportOrderId, general.OrderNumber));
        Assert.Equal("Thalie Peeters", general.CreatedByName);
        Assert.False(general.HasFile);
        Assert.False(general.CustomerVisible); // a new document is internal unless the uploader says otherwise

        var listed = Assert.Single((await h.Documents().ListForDossierAsync(dossier.Id, CancellationToken.None))!);
        Assert.Equal(general.Id, listed.Id);
        Assert.Empty((await h.Documents().ListAsync(orders[0], CancellationToken.None))!);
        Assert.Empty((await h.Documents().ListAsync(orders[1], CancellationToken.None))!);

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(1, detail.DocumentCount);
        Assert.Equal(new[] { "Other" }, detail.DocumentTypes);
        Assert.All(detail.Activities!, a => Assert.Equal(0, a.DocumentCount)); // dossier level is never counted per activity
    }

    [Fact]
    public async Task OrderDocument_BelongsToItsOrderOnly_AndCarriesTheOwningDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(2);

        // Through the ORDER route: the service resolves and stores the owning dossier itself.
        var viaOrder = (await h.Documents().CreateAsync(orders[0], new SaveTransportOrderDocumentRequest(
            TransportOrderDocumentType.Cmr, null, "CMR rit 1", null, null), CancellationToken.None))!;
        Assert.Equal((dossier.Id, DossierDocumentScope.Order), (viaOrder.DossierId, viaOrder.Scope));

        // Through the DOSSIER route, for order B.
        var viaDossier = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Leverbon rit 2", orders[1]), CancellationToken.None))!;
        Assert.Equal(DossierDocumentScope.Order, viaDossier.Scope);
        Assert.NotNull(viaDossier.OrderNumber);

        Assert.Equal(new[] { viaOrder.Id }, (await h.Documents().ListAsync(orders[0], CancellationToken.None))!.Select(d => d.Id));
        Assert.Equal(new[] { viaDossier.Id }, (await h.Documents().ListAsync(orders[1], CancellationToken.None))!.Select(d => d.Id));
        Assert.Equal(2, (await h.Documents().ListForDossierAsync(dossier.Id, CancellationToken.None))!.Count);

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(2, detail.DocumentCount);
        Assert.Equal(new[] { 0, 1, 1 }, detail.Activities!.OrderBy(a => a.Sequence).Select(a => a.DocumentCount));
    }

    [Fact]
    public async Task ActivityCard_ListsTheIssuedDocumentsOfItsOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(2);
        IssuedTransportDocument Issued(Guid orderId, IssuedTransportDocumentKind kind, string number, int minute) => new()
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId, DossierId = dossier.Id, Kind = kind,
            DocumentNumber = number, RequestId = Guid.NewGuid(), IssuedAt = Now.UtcDateTime.AddMinutes(minute),
        };
        h.Db.Context.IssuedTransportDocuments.AddRange(
            Issued(orders[0], IssuedTransportDocumentKind.Cmr, "CMR-2026-00002", 5),
            Issued(orders[0], IssuedTransportDocumentKind.DeliveryNote, "LB-2026-00001", 1));
        await h.Db.Context.SaveChangesAsync();

        var activities = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!.Activities!.OrderBy(a => a.Sequence).ToList();

        Assert.Empty(activities[0].IssuedDocuments!); // standalone activity: no order, no documents
        Assert.Equal(
            new[] { ("DeliveryNote", "LB-2026-00001"), ("Cmr", "CMR-2026-00002") },
            activities[1].IssuedDocuments!.Select(d => (d.Kind, d.DocumentNumber)));
        Assert.Empty(activities[2].IssuedDocuments!);
    }

    [Fact]
    public async Task LegacyOrderDocument_WithoutDossierId_IsStillListedAndCountedOnce()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(1);
        h.Db.Context.TransportOrderDocuments.Add(new TransportOrderDocument
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orders[0], DossierId = null, Title = "Oud document",
        });
        await h.Db.Context.SaveChangesAsync();

        var listed = Assert.Single((await h.Documents().ListForDossierAsync(dossier.Id, CancellationToken.None))!);
        Assert.Equal((dossier.Id, DossierDocumentScope.Order), (listed.DossierId, listed.Scope));
        Assert.Equal(1, (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!.DocumentCount);
    }

    [Fact]
    public async Task Create_ForAnOrderOfAnotherDossier_OrAnotherTenant_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(1);
        var (_, otherOrders) = await h.DossierWithOrdersAsync(1);

        var wrongDossier = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Documents().CreateForDossierAsync(dossier.Id, General("Fout", otherOrders[0]), CancellationToken.None));
        Assert.Contains("hoort niet bij dit dossier", wrongDossier.Message);

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() =>
            h.Documents().CreateForDossierAsync(dossier.Id, General("Fout", Guid.NewGuid()), CancellationToken.None));
        Assert.Empty(h.Db.Context.TransportOrderDocuments);
    }

    // ---- move: only the link changes ----

    [Fact]
    public async Task Move_OrderToDossier_KeepsFileAndVisibility_ChangesScopeAndCounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(2);
        var created = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan", orders[0], visible: true), CancellationToken.None))!;
        await AttachAsync(h.Documents(), created.Id, DossierDocumentScope.Order);
        var before = await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == created.Id);

        var moved = (await h.Documents().MoveAsync(created.Id, null, CancellationToken.None))!;

        var after = await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == created.Id);
        Assert.Equal((DossierDocumentScope.Dossier, (Guid?)null), (moved.Scope, moved.TransportOrderId));
        Assert.Equal((before.DocumentPath, before.FileName, before.ContentType, true), (after.DocumentPath, after.FileName, after.ContentType, after.CustomerVisible));
        Assert.Equal(dossier.Id, after.DossierId);
        var stillThere = await h.Documents().OpenFileAsync(created.Id, CancellationToken.None, DossierDocumentScope.Dossier); // the file is still there
        Assert.NotNull(stillThere);
        await stillThere!.Value.Content.DisposeAsync();
        Assert.Empty((await h.Documents().ListAsync(orders[0], CancellationToken.None))!);

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(1, detail.DocumentCount);
        Assert.All(detail.Activities!, a => Assert.Equal(0, a.DocumentCount));

        // ... and on to order B, then the same target again: idempotent, audited once per real move.
        var toB = (await h.Documents().MoveAsync(created.Id, orders[1], CancellationToken.None))!;
        var again = (await h.Documents().MoveAsync(created.Id, orders[1], CancellationToken.None))!;
        Assert.Equal((DossierDocumentScope.Order, orders[1]), (toB.Scope, toB.TransportOrderId!.Value));
        Assert.Equal(toB.TransportOrderId, again.TransportOrderId);
        var audits = await h.Db.Context.AuditLogs.Where(a => a.EntityType == "TransportOrderDocument" && a.Action == "Moved")
            .OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains("\"Scope\":\"Order\"", audits[0].OldValuesJson);
        Assert.Contains("\"Scope\":\"Dossier\"", audits[0].NewValuesJson);
    }

    [Fact]
    public async Task Move_ToAnOrderOfAnotherDossier_IsRefused_AndChangesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(1);
        var (_, otherOrders) = await h.DossierWithOrdersAsync(1);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan"), CancellationToken.None))!;

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Documents().MoveAsync(general.Id, otherOrders[0], CancellationToken.None));

        Assert.Contains("hetzelfde dossier", error.Message);
        Assert.Null((await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == general.Id)).TransportOrderId);
    }

    [Fact]
    public async Task Move_NeedsTheWriteRightOfBothScopes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(1);
        var orderDocument = (await h.Documents().CreateForDossierAsync(dossier.Id, General("CMR", orders[0]), CancellationToken.None))!;

        // Order rights only: the SOURCE is fine, the dossier-level TARGET is not.
        h.Permissions.Codes.Add(PermissionCodes.OrdersEdit);
        var refused = (await h.Controller().Move(orderDocument.Id, new MoveOrderDocumentRequest(null), CancellationToken.None)).Result;
        Assert.Equal(403, Assert.IsType<ObjectResult>(refused).StatusCode);
        Assert.Equal(orders[0], (await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync()).TransportOrderId);

        // Dossier rights only: now the ORDER-level source is not writable.
        h.Permissions.Codes.Clear();
        h.Permissions.Codes.Add(PermissionCodes.DossiersManage);
        refused = (await h.Controller().Move(orderDocument.Id, new MoveOrderDocumentRequest(null), CancellationToken.None)).Result;
        Assert.Equal(403, Assert.IsType<ObjectResult>(refused).StatusCode);

        // Both: the move goes through.
        h.Permissions.Codes.Add(PermissionCodes.OrdersEdit);
        var ok = Assert.IsType<OkObjectResult>((await h.Controller().Move(orderDocument.Id, new MoveOrderDocumentRequest(null), CancellationToken.None)).Result);
        Assert.Equal(DossierDocumentScope.Dossier, Assert.IsType<DossierDocumentDto>(ok.Value).Scope);
    }

    // ---- scope fences ----

    [Fact]
    public async Task DossierDocument_CannotBeTouchedThroughAnOrderScopedPath()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(1);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan"), CancellationToken.None))!;
        await AttachAsync(h.Documents(), general.Id, DossierDocumentScope.Dossier);
        var rename = new SaveTransportOrderDocumentRequest(TransportOrderDocumentType.Other, null, "Gekaapt", null, null);

        // The default scope of every by-id operation is ORDER: a dossier document is simply not found.
        Assert.False(await h.Documents().DeleteAsync(general.Id, CancellationToken.None));
        Assert.Null(await h.Documents().UpdateAsync(general.Id, rename, CancellationToken.None));
        Assert.Null(await h.Documents().OpenFileAsync(general.Id, CancellationToken.None));
        Assert.False(await h.Documents().RemoveFileAsync(general.Id, CancellationToken.None));

        var untouched = await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == general.Id);
        Assert.Equal(("Plan", false), (untouched.Title, untouched.IsDeleted));
        Assert.NotNull(untouched.DocumentPath);

        // ... and through the dossier scope it works.
        Assert.True(await h.Documents().DeleteAsync(general.Id, CancellationToken.None, DossierDocumentScope.Dossier));
    }

    [Fact]
    public async Task Update_WithoutAVisibilityValue_LeavesADossierDocumentsPublicationUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithOrdersAsync(0);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan", visible: true), CancellationToken.None))!;

        var renamed = (await h.Documents().UpdateAsync(general.Id,
            new SaveTransportOrderDocumentRequest(TransportOrderDocumentType.Other, null, "Plan (herzien)", null, null),
            CancellationToken.None, DossierDocumentScope.Dossier))!;

        Assert.Equal(("Plan (herzien)", true, DossierDocumentScope.Dossier), (renamed.Title, renamed.CustomerVisible, renamed.Scope));
        var withdrawn = (await h.Documents().UpdateAsync(general.Id,
            new SaveTransportOrderDocumentRequest(TransportOrderDocumentType.Other, null, "Plan (herzien)", null, null, CustomerVisible: false),
            CancellationToken.None, DossierDocumentScope.Dossier))!;
        Assert.False(withdrawn.CustomerVisible);
    }

    [Fact]
    public async Task ClosedDossier_RefusesDossierLevelWritesAndMoves_ButStillReads()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(1);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan"), CancellationToken.None))!;
        await h.Db.Context.TransportDossiers.Where(d => d.Id == dossier.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DossierStatus.Closed));

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Documents().CreateForDossierAsync(dossier.Id, General("Nieuw"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Documents().DeleteAsync(general.Id, CancellationToken.None, DossierDocumentScope.Dossier));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Documents().MoveAsync(general.Id, orders[0], CancellationToken.None));

        Assert.Single((await h.Documents().ListForDossierAsync(dossier.Id, CancellationToken.None))!);
    }

    // ---- permission matrix of the flat routes ----

    [Theory]
    [InlineData(DossierDocumentScope.Dossier, OrderDocumentOperation.Download, new[] { PermissionCodes.DossiersView, PermissionCodes.DossiersManage })]
    [InlineData(DossierDocumentScope.Dossier, OrderDocumentOperation.Upload, new[] { PermissionCodes.DossiersManage })]
    [InlineData(DossierDocumentScope.Dossier, OrderDocumentOperation.Write, new[] { PermissionCodes.DossiersManage })]
    [InlineData(DossierDocumentScope.Order, OrderDocumentOperation.Download, new[] { PermissionCodes.OrdersView, PermissionCodes.OrdersManage })]
    [InlineData(DossierDocumentScope.Order, OrderDocumentOperation.Upload, new[] { PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage })]
    [InlineData(DossierDocumentScope.Order, OrderDocumentOperation.Write, new[] { PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage })]
    public void FlatRoutePermissionMatrix(DossierDocumentScope scope, OrderDocumentOperation operation, string[] expected) =>
        Assert.Equal(expected, OrderDocumentAccessResolver.RequiredPermissions(scope, operation));

    [Fact]
    public async Task FlatRoutes_DecideByTheRowsScope()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(1);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan"), CancellationToken.None))!;
        var orderDocument = (await h.Documents().CreateForDossierAsync(dossier.Id, General("CMR", orders[0]), CancellationToken.None))!;
        await AttachAsync(h.Documents(), general.Id, DossierDocumentScope.Dossier);
        await AttachAsync(h.Documents(), orderDocument.Id, DossierDocumentScope.Order);
        var rename = new SaveTransportOrderDocumentRequest(TransportOrderDocumentType.Other, null, "Nieuwe titel", null, null);

        // Full ORDER rights say nothing about a dossier document ...
        h.Permissions.Codes.UnionWith([PermissionCodes.OrdersView, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage]);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await h.Controller().DownloadFile(general.Id, CancellationToken.None)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>((await h.Controller().Update(general.Id, rename, CancellationToken.None)).Result).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await h.Controller().Delete(general.Id, CancellationToken.None)).StatusCode);
        await Assert.IsType<FileStreamResult>(await h.Controller().DownloadFile(orderDocument.Id, CancellationToken.None)).FileStream.DisposeAsync();

        // ... dossiers.view downloads a dossier document but writes nothing, and opens no order document ...
        h.Permissions.Codes.Clear();
        h.Permissions.Codes.Add(PermissionCodes.DossiersView);
        await Assert.IsType<FileStreamResult>(await h.Controller().DownloadFile(general.Id, CancellationToken.None)).FileStream.DisposeAsync();
        Assert.Equal(403, Assert.IsType<ObjectResult>(await h.Controller().Delete(general.Id, CancellationToken.None)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await h.Controller().DownloadFile(orderDocument.Id, CancellationToken.None)).StatusCode);

        // ... dossiers.manage writes the dossier document, still not the order document.
        h.Permissions.Codes.Add(PermissionCodes.DossiersManage);
        Assert.IsType<OkObjectResult>((await h.Controller().Update(general.Id, rename, CancellationToken.None)).Result);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await h.Controller().Delete(orderDocument.Id, CancellationToken.None)).StatusCode);
        Assert.IsType<NoContentResult>(await h.Controller().Delete(general.Id, CancellationToken.None));

        Assert.IsType<NotFoundResult>(await h.Controller().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    // ---- tenant isolation ----

    [Fact]
    public async Task AnotherTenant_SeesNothing_AndCanChangeNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, orders) = await h.DossierWithOrdersAsync(1);
        var general = (await h.Documents().CreateForDossierAsync(dossier.Id, General("Plan"), CancellationToken.None))!;
        var foreign = h.Documents(tenantId: Guid.NewGuid());

        Assert.Null(await foreign.ListForDossierAsync(dossier.Id, CancellationToken.None));
        Assert.Null(await foreign.CreateForDossierAsync(dossier.Id, General("Indringer"), CancellationToken.None));
        Assert.Null(await foreign.GetScopeAsync(general.Id, CancellationToken.None));
        Assert.Null(await foreign.MoveAsync(general.Id, orders[0], CancellationToken.None));
        Assert.False(await foreign.DeleteAsync(general.Id, CancellationToken.None, DossierDocumentScope.Dossier));

        // The own tenant may not point a document at a foreign tenant's order either.
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Documents().MoveAsync(general.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Single(h.Db.Context.TransportOrderDocuments);
    }

    // ---- DossierId follows the owning dossier ----

    [Fact]
    public async Task UnlinkingAndRelinkingAnOrder_KeepsItsDocumentsOnTheOwningDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (first, orders) = await h.DossierWithOrdersAsync(1);
        var (second, _) = await h.DossierWithOrdersAsync(0);
        var orderDocument = (await h.Documents().CreateForDossierAsync(first.Id, General("CMR", orders[0]), CancellationToken.None))!;
        var general = (await h.Documents().CreateForDossierAsync(first.Id, General("Plan"), CancellationToken.None))!;

        async Task<Guid?> DossierOfAsync(Guid documentId) =>
            (await h.Db.Context.TransportOrderDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId)).DossierId;

        // A second link does not change the owner (the oldest link keeps winning).
        await h.Dossiers().LinkOrderAsync(second.Id, new LinkDossierOrderRequest(orders[0]), CancellationToken.None);
        Assert.Equal(first.Id, await DossierOfAsync(orderDocument.Id));

        // Removing the FIRST link makes the second dossier the owner: the order document follows,
        // the document of the first dossier as a whole stays where it is.
        await h.Dossiers().UnlinkOrderAsync(first.Id, orders[0], CancellationToken.None);
        Assert.Equal(second.Id, await DossierOfAsync(orderDocument.Id));
        Assert.Equal(first.Id, await DossierOfAsync(general.Id));
        Assert.Equal(1, (await h.Dossiers().GetAsync(first.Id, CancellationToken.None))!.DocumentCount);
        Assert.Equal(1, (await h.Dossiers().GetAsync(second.Id, CancellationToken.None))!.DocumentCount);

        // No dossier left → NULL (still reachable through the order); linking again adopts it.
        await h.Dossiers().UnlinkOrderAsync(second.Id, orders[0], CancellationToken.None);
        Assert.Null(await DossierOfAsync(orderDocument.Id));
        Assert.Single((await h.Documents().ListAsync(orders[0], CancellationToken.None))!);
        await h.Dossiers().LinkOrderAsync(first.Id, new LinkDossierOrderRequest(orders[0]), CancellationToken.None);
        Assert.Equal(first.Id, await DossierOfAsync(orderDocument.Id));
    }

    // ---- migration data step ----

    [Fact]
    public async Task Backfill_UsesTheOwningDossierPrecedence_TouchesNothingElse_AndIsIdempotent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var context = h.Db.Context;
        Guid NewOrder(string number)
        {
            var id = Guid.NewGuid();
            context.TransportOrders.Add(new TransportOrder
            {
                Id = id, TenantId = h.TenantId, CustomerId = h.CustomerId, OrderNumber = number, OrderDate = new DateOnly(2026, 9, 1),
            });
            return id;
        }

        Guid NewDossier(string number, Guid? originOrderId = null)
        {
            var id = Guid.NewGuid();
            context.TransportDossiers.Add(new TransportDossier
            {
                Id = id, TenantId = h.TenantId, DossierNumber = number, Title = number, OriginTransportOrderId = originOrderId,
            });
            return id;
        }

        var wrapped = NewOrder("ORD-W");
        var linkedTwice = NewOrder("ORD-L");
        var loose = NewOrder("ORD-X");
        var userDossier = NewDossier("DOS-USER");
        var wrapper = NewDossier("DOS-WRAP", wrapped);
        var later = NewDossier("DOS-LATER");
        await context.SaveChangesAsync();

        // The wrapped order ALSO has an older user link: the wrapper must still win.
        var links = new[]
        {
            new DossierOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = userDossier, TransportOrderId = wrapped },
            new DossierOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = wrapper, TransportOrderId = wrapped },
            new DossierOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = later, TransportOrderId = linkedTwice },
            new DossierOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = userDossier, TransportOrderId = linkedTwice },
        };
        context.DossierOrders.AddRange(links);
        TransportOrderDocument Document(Guid orderId, string title) => new()
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId, Title = title,
            DocumentPath = $"order-documents/{title}.pdf", FileName = $"{title}.pdf", CustomerVisible = true,
        };
        var documents = new[] { Document(wrapped, "w"), Document(linkedTwice, "l"), Document(loose, "x") };
        context.TransportOrderDocuments.AddRange(documents);
        await context.SaveChangesAsync();
        // Oldest link wins: make the "later" dossier's link the NEWER one explicitly.
        await context.DossierOrders.Where(l => l.Id == links[2].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, l => l.CreatedAt.AddHours(1)));
        var updatedAt = documents.ToDictionary(d => d.Id, d => d.UpdatedAt);

        var firstRun = await context.Database.ExecuteSqlRawAsync(OrderDocumentDossierBackfill.Sql);
        var secondRun = await context.Database.ExecuteSqlRawAsync(OrderDocumentDossierBackfill.Sql);

        var rows = await context.TransportOrderDocuments.AsNoTracking().ToDictionaryAsync(d => d.Title);
        Assert.Equal((2, 0), (firstRun, secondRun));
        Assert.Equal(wrapper, rows["w"].DossierId);
        Assert.Equal(userDossier, rows["l"].DossierId);
        Assert.Null(rows["x"].DossierId); // no dossier: stays reachable through its order
        Assert.All(rows.Values, d =>
        {
            Assert.NotNull(d.TransportOrderId);
            Assert.Equal(($"order-documents/{d.Title}.pdf", $"{d.Title}.pdf", true, false), (d.DocumentPath, d.FileName, d.CustomerVisible, d.IsDeleted));
            Assert.Equal(updatedAt[d.Id], d.UpdatedAt);
        });

        // The runtime resolver agrees with the SQL.
        Assert.Equal(wrapper, (await OwningDossierResolver.ResolveAsync(context, h.TenantId, wrapped, CancellationToken.None))!.Id);
        Assert.Equal(userDossier, (await OwningDossierResolver.ResolveAsync(context, h.TenantId, linkedTwice, CancellationToken.None))!.Id);
        Assert.Null(await OwningDossierResolver.ResolveAsync(context, h.TenantId, loose, CancellationToken.None));
    }
}
