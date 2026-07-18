using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Lookups;

public class LookupServiceTests
{
    private static LookupService<Department> CreateSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenantContext = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(null));
        return new LookupService<Department>(db.Context, tenantContext, audit);
    }

    private static async Task<Guid> SeedTenantAsync(SqliteTestDbContext db, string slug)
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    private static CreateLookupRequest NewRequest(string code, string name) => new(code, name, null, true, 0);

    [Fact]
    public async Task CreateAsync_PersistsAndStampsAuditTimestamps()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t");
        var sut = CreateSut(db, tenantId);

        var result = await sut.CreateAsync(NewRequest("PLAN", "Planning"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.Success, result.Status);
        var stored = await db.Context.Departments.SingleAsync();
        Assert.Equal("PLAN", stored.Code);
        Assert.Equal(tenantId, stored.TenantId);
        Assert.NotEqual(default, stored.CreatedAt);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateCode_CaseInsensitive()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t");
        var sut = CreateSut(db, tenantId);

        await sut.CreateAsync(NewRequest("PLAN", "Planning"), CancellationToken.None);
        var duplicate = await sut.CreateAsync(NewRequest("plan", "Planning 2"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.DuplicateCode, duplicate.Status);
        Assert.Equal(1, await db.Context.Departments.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_AndHidesFromReads()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t");
        var sut = CreateSut(db, tenantId);
        var created = await sut.CreateAsync(NewRequest("PLAN", "Planning"), CancellationToken.None);

        var deleted = await sut.DeleteAsync(created.Item!.Id, CancellationToken.None);

        Assert.True(deleted);
        // Hidden from ordinary reads via the global query filter...
        Assert.Null(await sut.GetByIdAsync(created.Item!.Id, CancellationToken.None));
        // ...but the row still physically exists, flagged deleted.
        var raw = await db.Context.Departments.IgnoreQueryFilters().SingleAsync();
        Assert.True(raw.IsDeleted);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task CreateAsync_CanReuseCode_AfterSoftDelete()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t");
        var sut = CreateSut(db, tenantId);
        var created = await sut.CreateAsync(NewRequest("PLAN", "Planning"), CancellationToken.None);
        await sut.DeleteAsync(created.Item!.Id, CancellationToken.None);

        var recreated = await sut.CreateAsync(NewRequest("PLAN", "Planning (opnieuw)"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.Success, recreated.Status);
    }

    [Fact]
    public async Task SearchAsync_IsTenantScoped()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a");
        var tenantB = await SeedTenantAsync(db, "b");
        await CreateSut(db, tenantA).CreateAsync(NewRequest("PLAN", "Planning A"), CancellationToken.None);

        var resultForB = await CreateSut(db, tenantB).SearchAsync(null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Empty(resultForB.Items);
    }

    [Fact]
    public async Task ListOptionsAsync_ReturnsOnlyActive_Ordered()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t");
        var sut = CreateSut(db, tenantId);
        await sut.CreateAsync(new CreateLookupRequest("B", "Beta", null, true, 2), CancellationToken.None);
        await sut.CreateAsync(new CreateLookupRequest("A", "Alpha", null, true, 1), CancellationToken.None);
        await sut.CreateAsync(new CreateLookupRequest("X", "Inactive", null, false, 0), CancellationToken.None);

        var options = await sut.ListOptionsAsync(CancellationToken.None);

        Assert.Equal(2, options.Count);
        Assert.Equal("A", options[0].Code); // lower SortOrder first
        Assert.Equal("B", options[1].Code);
    }
}
