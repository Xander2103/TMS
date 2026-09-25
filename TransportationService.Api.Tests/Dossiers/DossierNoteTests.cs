using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Master sprint 2026-09-21 D7 — dossier notes (dossier-level or about one activity): CRUD,
/// dossier/tenant fences, author names, the set-based previews/counts on the dossier DTO and an
/// audit trail that never stores the full note text.
/// </summary>
public class DossierNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(SqliteTestDbContext Db, TestClock Clock, Guid TenantId, Guid CustomerId, Guid UserId, PermissionSet Permissions)
    {
        private DevTenantContext Tenant => new(TenantId);

        private AuditService Audit(ITenantContext tenant, Guid? userId) => new(Db.Context, tenant, new DevCurrentUserContext(userId));

        public DossierService Dossiers() => new(Db.Context, Tenant, Audit(Tenant, null), Clock);

        public DossierActivityService Activities()
        {
            var tenant = Tenant;
            var orders = new Api.Modules.Orders.Services.TransportOrderService(Db.Context, tenant, Audit(tenant, null), Clock);
            return new DossierActivityService(Db.Context, tenant, Audit(tenant, null), Dossiers(), orders, Clock);
        }

        public DossierNoteService Notes(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierNoteService(Db.Context, tenant, Audit(tenant, UserId), new DevCurrentUserContext(UserId), Permissions);
        }

        /// <summary>The interceptor stamps CreatedAt from the system clock; ordering tests move a note back in time.</summary>
        public Task BackdateAsync(Guid noteId, TimeSpan by) =>
            Db.Context.DossierNotes.Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, n => n.CreatedAt.AddMinutes(-by.TotalMinutes)));

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        public async Task<DossierDetailDto> DossierWithAsync(string typeCode) =>
            await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId, ActivityTypeId: await TypeIdAsync(typeCode)), CancellationToken.None);
    }

    private static async Task<Harness> SeedAsync()
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
        var permissions = new PermissionSet();
        permissions.Codes.Add(PermissionCodes.DossiersManage);
        return new Harness(db, new TestClock(Now), tenantId, customerId, userId, permissions);
    }

    [Fact]
    public async Task Crud_DossierLevelAndActivityNotes_NewestFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activityId = dossier.Activities!.Single().Id;

        var general = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("  Klant belt vooraf.  "), CancellationToken.None))!;
        var onActivity = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Rek 12, niet stapelen", activityId), CancellationToken.None))!;
        await h.BackdateAsync(general.Id, TimeSpan.FromMinutes(5));

        Assert.Equal("Klant belt vooraf.", general.Text);
        Assert.Null(general.DossierActivityId);
        Assert.Equal(activityId, onActivity.DossierActivityId);
        Assert.Equal((dossier.Id, "Thalie Peeters", true, true), (general.DossierId, general.AuthorName, general.CanEdit, general.CanDelete));

        var all = (await h.Notes().ListAsync(dossier.Id, null, CancellationToken.None))!;
        Assert.Equal(new[] { onActivity.Id, general.Id }, all.Select(n => n.Id));
        var filtered = (await h.Notes().ListAsync(dossier.Id, activityId, CancellationToken.None))!;
        Assert.Equal(onActivity.Id, Assert.Single(filtered).Id);

        var updated = (await h.Notes().UpdateAsync(dossier.Id, general.Id, new UpdateDossierNoteRequest("Klant belt een uur vooraf."), CancellationToken.None))!;
        Assert.Equal("Klant belt een uur vooraf.", updated.Text);
        Assert.True(updated.UpdatedAt >= updated.CreatedAt);

        Assert.True(await h.Notes().DeleteAsync(dossier.Id, onActivity.Id, CancellationToken.None));
        Assert.Equal(general.Id, Assert.Single((await h.Notes().ListAsync(dossier.Id, null, CancellationToken.None))!).Id);
        // Soft delete: the row stays.
        Assert.True((await h.Db.Context.DossierNotes.IgnoreQueryFilters().SingleAsync(n => n.Id == onActivity.Id)).IsDeleted);

        // The legacy free-text columns are never written by the note endpoints.
        Assert.Null((await h.Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == dossier.Id)).Notes);
        Assert.Null((await h.Db.Context.DossierActivities.AsNoTracking().SingleAsync(a => a.Id == activityId)).Notes);
    }

    [Fact]
    public async Task Validation_TextRequired_Max4000()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("   "), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest(new string('x', 4001)), CancellationToken.None));
        Assert.NotNull(await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest(new string('x', 4000)), CancellationToken.None));
    }

    [Fact]
    public async Task AnActivityOfAnotherDossier_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var other = await h.DossierWithAsync("KRAANWERK");

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().CreateAsync(
            dossier.Id, new CreateDossierNoteRequest("Verkeerde activiteit", other.Activities!.Single().Id), CancellationToken.None));

        Assert.Contains("hoort niet bij dit dossier", ex.Message);
        Assert.Equal(0, await h.Db.Context.DossierNotes.CountAsync());
    }

    [Fact]
    public async Task ANoteOfAnotherDossierOrTenant_IsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var other = await h.DossierWithAsync("KRAANWERK");
        var note = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Van dossier 1"), CancellationToken.None))!;

        // Addressed through ANOTHER dossier of the same tenant.
        Assert.Null(await h.Notes().UpdateAsync(other.Id, note.Id, new UpdateDossierNoteRequest("Gekaapt"), CancellationToken.None));
        Assert.False(await h.Notes().DeleteAsync(other.Id, note.Id, CancellationToken.None));
        // Another tenant sees neither the dossier nor the note.
        var foreign = h.Notes(Guid.NewGuid());
        Assert.Null(await foreign.ListAsync(dossier.Id, null, CancellationToken.None));
        Assert.Null(await foreign.CreateAsync(dossier.Id, new CreateDossierNoteRequest("Vreemd"), CancellationToken.None));
        Assert.Null(await foreign.UpdateAsync(dossier.Id, note.Id, new UpdateDossierNoteRequest("Vreemd"), CancellationToken.None));
        Assert.False(await foreign.DeleteAsync(dossier.Id, note.Id, CancellationToken.None));

        Assert.Equal("Van dossier 1", (await h.Db.Context.DossierNotes.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public async Task ClosedDossier_CanBeRead_ButNotWritten()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var note = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Voor sluiting"), CancellationToken.None))!;
        await DossierTestLifecycle.CloseAsync(h.Db.Context, h.TenantId, dossier.Id);

        var read = Assert.Single((await h.Notes().ListAsync(dossier.Id, null, CancellationToken.None))!);
        Assert.False(read.CanEdit);
        Assert.False(read.CanDelete);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Na sluiting"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().UpdateAsync(dossier.Id, note.Id, new UpdateDossierNoteRequest("Na sluiting"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Notes().DeleteAsync(dossier.Id, note.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CanEditAndCanDelete_FollowTheManagePermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Alleen lezen"), CancellationToken.None);
        h.Permissions.Codes.Clear();

        var note = Assert.Single((await h.Notes().ListAsync(dossier.Id, null, CancellationToken.None))!);

        Assert.False(note.CanEdit);
        Assert.False(note.CanDelete);
    }

    [Fact]
    public async Task DossierDto_CarriesCountsAndTheNewestPreview_PerActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        dossier = (await h.Activities().AddAsync(dossier.Id,
            new SaveDossierActivityRequest(await h.TypeIdAsync("KRAANWERK"), Version: dossier.Version), CancellationToken.None))!;
        var storage = dossier.Activities!.Single(a => a.ActivityTypeCode == "OPSLAG");
        var crane = dossier.Activities!.Single(a => a.ActivityTypeCode == "KRAANWERK");
        Assert.Equal((0, null, null), (storage.NoteCount, storage.LatestNotePreview, storage.LatestNoteAt));

        await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Algemeen 1"), CancellationToken.None);
        await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Algemeen 2"), CancellationToken.None);
        var oldest = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest("Oudste", storage.Id), CancellationToken.None))!;
        await h.BackdateAsync(oldest.Id, TimeSpan.FromMinutes(10));
        var longText = "Regel één\r\nregel twee\n\n   " + new string('x', 300);
        var newest = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest(longText, storage.Id), CancellationToken.None))!;

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;

        Assert.Equal(2, detail.NoteCount); // dossier-level only
        var withNotes = detail.Activities!.Single(a => a.Id == storage.Id);
        Assert.Equal(2, withNotes.NoteCount);
        Assert.Equal(newest.CreatedAt, withNotes.LatestNoteAt);
        Assert.Equal(160, withNotes.LatestNotePreview!.Length);
        Assert.StartsWith("Regel één regel twee xxx", withNotes.LatestNotePreview);
        Assert.DoesNotContain('\n', withNotes.LatestNotePreview);
        var without = detail.Activities!.Single(a => a.Id == crane.Id);
        Assert.Equal((0, null, null), (without.NoteCount, without.LatestNotePreview, without.LatestNoteAt));
    }

    [Fact]
    public async Task AuditRows_AreWritten_WithoutTheFullText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var secret = "Begin van de notitie. " + new string('s', 200) + " GEHEIM-EINDE";

        var note = (await h.Notes().CreateAsync(dossier.Id, new CreateDossierNoteRequest(secret), CancellationToken.None))!;
        await h.Notes().UpdateAsync(dossier.Id, note.Id, new UpdateDossierNoteRequest(secret + " bis"), CancellationToken.None);
        await h.Notes().DeleteAsync(dossier.Id, note.Id, CancellationToken.None);

        var rows = await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "DossierNote" && a.EntityId == note.Id.ToString()).ToListAsync();
        Assert.Equal(new[] { "Created", "Deleted", "Updated" }, rows.Select(r => r.Action).OrderBy(a => a));
        foreach (var row in rows)
        {
            var payload = (row.OldValuesJson ?? string.Empty) + (row.NewValuesJson ?? string.Empty);
            Assert.DoesNotContain("GEHEIM-EINDE", payload);
            Assert.Contains("TextLength", payload, StringComparison.OrdinalIgnoreCase);
        }
    }
}
