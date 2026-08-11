using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

public class ActivityTypeSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 11, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedTenantAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        return (db, tenantId);
    }

    [Fact]
    public async Task Seeds_TenDefaults_WithExactlyOneDefaultTransport()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;

        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);

        var types = await db.Context.ActivityTypes.Where(t => t.TenantId == tenantId).ToListAsync();
        Assert.Equal(10, types.Count);
        Assert.Single(types, t => t.IsSystemDefaultTransport);
        Assert.Equal("DIRECT_TRANSPORT", types.Single(t => t.IsSystemDefaultTransport).Code);
        Assert.Equal(4, types.Count(t => t.IsQuickStart));
        Assert.True(types.Single(t => t.Code == "KRAANWERK").AllowsDuration);
        Assert.False(types.Single(t => t.Code == "KRAANWERK").HasStops);
    }

    [Fact]
    public async Task Seeding_IsIdempotent_AndNeverResurrectsDeletedTypes()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var seeder = new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId));
        await seeder.EnsureSeededAsync(CancellationToken.None);

        var plateau = await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantId && t.Code == "PLATEAU");
        plateau.IsDeleted = true;
        var renamed = await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantId && t.Code == "OPSLAG");
        renamed.Name = "Warehousing";
        await db.Context.SaveChangesAsync();

        await seeder.EnsureSeededAsync(CancellationToken.None);

        var visible = await db.Context.ActivityTypes.Where(t => t.TenantId == tenantId).ToListAsync();
        Assert.Equal(9, visible.Count); // PLATEAU stays deleted
        Assert.Equal("Warehousing", visible.Single(t => t.Code == "OPSLAG").Name); // edit preserved
    }

    [Fact]
    public async Task Seeding_RespectsTenantChoiceOfDefaultTransport()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var seeder = new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId));
        await seeder.EnsureSeededAsync(CancellationToken.None);

        // Tenant moves the default flag to EXPRESS and deletes DIRECT_TRANSPORT. Clear-then-set
        // in two saves: the filtered unique index is checked per statement, so the service
        // layer must (and does) release the old default before assigning the new one.
        var direct = await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantId && t.Code == "DIRECT_TRANSPORT");
        direct.IsSystemDefaultTransport = false;
        direct.IsDeleted = true;
        await db.Context.SaveChangesAsync();
        var express = await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantId && t.Code == "EXPRESS");
        express.IsSystemDefaultTransport = true;
        await db.Context.SaveChangesAsync();

        await seeder.EnsureSeededAsync(CancellationToken.None);

        var defaults = await db.Context.ActivityTypes
            .Where(t => t.TenantId == tenantId && t.IsSystemDefaultTransport).ToListAsync();
        Assert.Single(defaults);
        Assert.Equal("EXPRESS", defaults[0].Code);
    }

    [Fact]
    public async Task Seeding_IsPerTenant_AndTenantScoped()
    {
        var (db, tenantA) = await SeedTenantAsync();
        using var _ = db;
        var tenantB = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantA)).EnsureSeededAsync(CancellationToken.None);
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantB)).EnsureSeededAsync(CancellationToken.None);

        Assert.Equal(10, await db.Context.ActivityTypes.CountAsync(t => t.TenantId == tenantA));
        Assert.Equal(10, await db.Context.ActivityTypes.CountAsync(t => t.TenantId == tenantB));

        // Tenant B edits stay invisible to tenant A's rows.
        var bStorage = await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantB && t.Code == "OPSLAG");
        bStorage.Name = "Container yard";
        await db.Context.SaveChangesAsync();
        Assert.Equal("Opslag", (await db.Context.ActivityTypes.SingleAsync(t => t.TenantId == tenantA && t.Code == "OPSLAG")).Name);
    }
}
