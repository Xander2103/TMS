using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

public class ActivityTypeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId)
    {
        public ActivityTypeService Sut(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var user = new DevCurrentUserContext(UserId);
            return new ActivityTypeService(Db.Context, tenant,
                new ActivityTypeSeeder(Db.Context, tenant),
                new AuditService(Db.Context, tenant, user));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, Guid.NewGuid());
    }

    private static SaveActivityTypeRequest SaveRequestOf(ActivityTypeDto dto) => new(
        dto.Code, dto.Name, dto.IsActive, dto.SortOrder, dto.Icon, dto.KpiCategory,
        dto.HasStops, dto.SupportsGoods, dto.PlanningRelevant, dto.WarehouseRelevant,
        dto.AllowsDuration, dto.IsQuickStart, dto.QuickStartOrder, dto.IsSystemDefaultTransport);

    [Fact]
    public async Task List_SeedsTheDefaultCatalogueOnFirstRead_AndFiltersInactive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var all = await sut.ListAsync(includeInactive: true, CancellationToken.None);
        Assert.Equal(10, all.Count);
        Assert.Single(all, t => t.IsSystemDefaultTransport);

        // Deactivate a non-default type; the active list hides it, includeInactive shows it.
        var express = all.Single(t => t.Code == "EXPRESS");
        await sut.UpdateAsync(express.Id, SaveRequestOf(express) with { IsActive = false }, CancellationToken.None);

        var active = await sut.ListAsync(includeInactive: false, CancellationToken.None);
        Assert.Equal(9, active.Count);
        Assert.DoesNotContain(active, t => t.Code == "EXPRESS");
        Assert.Contains(await sut.ListAsync(includeInactive: true, CancellationToken.None), t => t.Code == "EXPRESS");
    }

    [Fact]
    public async Task Create_And_Update_RoundTripAllFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var created = await sut.CreateAsync(new SaveActivityTypeRequest(
            "CONTAINER", "Containervervoer", IsActive: true, SortOrder: 20,
            Icon: "box", KpiCategory: "Container",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: true,
            AllowsDuration: false, IsQuickStart: true, QuickStartOrder: 5), CancellationToken.None);

        Assert.Equal("CONTAINER", created.Code);
        Assert.Equal("Containervervoer", created.Name);
        Assert.True(created.HasStops);
        Assert.True(created.IsQuickStart);
        Assert.Equal(5, created.QuickStartOrder);
        Assert.False(created.IsSystemDefaultTransport);

        var updated = await sut.UpdateAsync(created.Id, SaveRequestOf(created) with
        {
            Name = "Containers",
            Icon = "ship",
            AllowsDuration = true,
            IsQuickStart = false,
        }, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Containers", updated!.Name);
        Assert.Equal("ship", updated.Icon);
        Assert.True(updated.AllowsDuration);
        Assert.False(updated.IsQuickStart);

        // Every mutation is audited.
        var audits = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "ActivityType" && a.EntityId == created.Id.ToString()).ToListAsync();
        Assert.Contains(audits, a => a.Action == "Created");
        Assert.Contains(audits, a => a.Action == "Updated");
    }

    [Fact]
    public async Task Update_RefusesCodeChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var opslag = (await sut.ListAsync(true, CancellationToken.None)).Single(t => t.Code == "OPSLAG");

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.UpdateAsync(opslag.Id, SaveRequestOf(opslag) with { Code = "WAREHOUSING" }, CancellationToken.None));

        Assert.Equal("De code van een activiteitstype kan niet gewijzigd worden.", ex.Message);
        Assert.Contains("code", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_RefusesDuplicateCode_CaseInsensitive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.ListAsync(true, CancellationToken.None); // seed

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(new SaveActivityTypeRequest("opslag", "Tweede opslag"), CancellationToken.None));

        Assert.Equal("Er bestaat al een activiteitstype met deze code.", ex.Message);

        // Validation of the field constraints uses the same Dutch field errors.
        var tooLong = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(new SaveActivityTypeRequest(new string('X', 51), "Naam"), CancellationToken.None));
        Assert.Contains("code", tooLong.FieldErrors!.Keys);
        var noName = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(new SaveActivityTypeRequest("NIEUW", "  "), CancellationToken.None));
        Assert.Contains("name", noName.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Delete_RefusedWhileInUse_AllowedAfterwards()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var types = await sut.ListAsync(true, CancellationToken.None);
        var kraanwerk = types.Single(t => t.Code == "KRAANWERK");

        var dossierId = Guid.NewGuid();
        h.Db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = h.TenantId, DossierNumber = "DOS-0001", Title = "Werf Nexans",
        });
        var activity = new DossierActivity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = dossierId,
            ActivityTypeId = kraanwerk.Id, Sequence = 1,
        };
        h.Db.Context.DossierActivities.Add(activity);
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.DeleteAsync(kraanwerk.Id, CancellationToken.None));
        Assert.Equal("Dit activiteitstype is in gebruik en kan niet verwijderd worden.", ex.Message);

        // A soft-deleted activity no longer blocks the delete. Clear the tracker afterwards:
        // production runs each request on a fresh context, and a stale tracked (soft-deleted)
        // dependent would otherwise trip EF's in-memory required-association fixup on Remove.
        h.Db.Context.Remove(activity);
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        Assert.True(await sut.DeleteAsync(kraanwerk.Id, CancellationToken.None));
        Assert.DoesNotContain(await sut.ListAsync(true, CancellationToken.None), t => t.Code == "KRAANWERK");
    }

    [Fact]
    public async Task DefaultTransport_CannotBeDeactivatedDeletedOrUnflagged_WhileItIsTheOnlyOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var direct = (await sut.ListAsync(true, CancellationToken.None)).Single(t => t.IsSystemDefaultTransport);

        var deactivate = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.UpdateAsync(direct.Id, SaveRequestOf(direct) with { IsActive = false }, CancellationToken.None));
        Assert.Equal("Er moet altijd één actief standaard transporttype zijn.", deactivate.Message);

        var unflag = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.UpdateAsync(direct.Id, SaveRequestOf(direct) with { IsSystemDefaultTransport = false }, CancellationToken.None));
        Assert.Equal("Er moet altijd één actief standaard transporttype zijn.", unflag.Message);

        var delete = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.DeleteAsync(direct.Id, CancellationToken.None));
        Assert.Equal("Er moet altijd één actief standaard transporttype zijn.", delete.Message);
    }

    [Fact]
    public async Task DefaultTransport_MovesViaClearThenSet_AndOldCarrierBecomesEditable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var types = await sut.ListAsync(true, CancellationToken.None);
        var direct = types.Single(t => t.Code == "DIRECT_TRANSPORT");
        var express = types.Single(t => t.Code == "EXPRESS");

        var moved = await sut.UpdateAsync(express.Id,
            SaveRequestOf(express) with { IsSystemDefaultTransport = true }, CancellationToken.None);
        Assert.True(moved!.IsSystemDefaultTransport);

        var after = await sut.ListAsync(true, CancellationToken.None);
        Assert.Single(after, t => t.IsSystemDefaultTransport);
        Assert.Equal("EXPRESS", after.Single(t => t.IsSystemDefaultTransport).Code);
        Assert.False(after.Single(t => t.Code == "DIRECT_TRANSPORT").IsSystemDefaultTransport);

        // The former default carries the flag no longer and may now be deactivated.
        var freedDirect = after.Single(t => t.Code == "DIRECT_TRANSPORT");
        var deactivated = await sut.UpdateAsync(freedDirect.Id,
            SaveRequestOf(freedDirect) with { IsActive = false }, CancellationToken.None);
        Assert.False(deactivated!.IsActive);

        // The flag never lands on an inactive type.
        var inactiveFlag = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.UpdateAsync(freedDirect.Id, SaveRequestOf(freedDirect) with
            {
                IsActive = false,
                IsSystemDefaultTransport = true,
            }, CancellationToken.None));
        Assert.Contains("isSystemDefaultTransport", inactiveFlag.FieldErrors!.Keys);
    }

    [Fact]
    public async Task TenantIsolation_ListUpdateAndDelete_NeverCrossTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenantB = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();

        var sutA = h.Sut();
        var sutB = h.Sut(tenantB);
        var listA = await sutA.ListAsync(true, CancellationToken.None);
        var listB = await sutB.ListAsync(true, CancellationToken.None);

        Assert.Equal(10, listA.Count);
        Assert.Equal(10, listB.Count);
        Assert.Empty(listA.Select(t => t.Id).Intersect(listB.Select(t => t.Id)));

        // Tenant A cannot touch tenant B's rows: update and delete resolve to not-found.
        var bOpslag = listB.Single(t => t.Code == "OPSLAG");
        Assert.Null(await sutA.UpdateAsync(bOpslag.Id,
            SaveRequestOf(bOpslag) with { Name = "Gekaapt" }, CancellationToken.None));
        Assert.False(await sutA.DeleteAsync(bOpslag.Id, CancellationToken.None));

        Assert.Equal("Opslag", (await sutB.ListAsync(true, CancellationToken.None)).Single(t => t.Code == "OPSLAG").Name);

        // A duplicate code check is tenant-local: A may reuse a code B also has.
        var aDeleted = listA.Single(t => t.Code == "OVERIG");
        await sutA.DeleteAsync(aDeleted.Id, CancellationToken.None);
        var recreated = await sutA.CreateAsync(new SaveActivityTypeRequest("OVERIG", "Overig nieuw"), CancellationToken.None);
        Assert.Equal("OVERIG", recreated.Code);
    }
}
