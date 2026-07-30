using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 3: structural tenant isolation (H1 architecture guards), cross-tenant reference rejection
/// (M12/L8/L9) and the globally unique Peppol participant identity (M1).
/// </summary>
public class Phase3TenantIsolationTests
{
    // ===================== H1 — architecture guards =====================

    [Fact]
    public void EveryTenantOwnedEntity_ExposesATenantIdColumn()
    {
        // A tenant-owned entity without a TenantId column cannot be isolated at all.
        using var db = new SqliteTestDbContext();
        var offenders = new List<string>();

        foreach (var entityType in db.Context.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (!typeof(ITenantOwned).IsAssignableFrom(clr))
            {
                continue;
            }

            if (entityType.FindProperty(nameof(ITenantOwned.TenantId)) is null)
            {
                offenders.Add(clr.Name);
            }
        }

        Assert.True(offenders.Count == 0, "Tenant-owned entities without a TenantId column: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EverySoftDeletableEntity_HasAQueryFilter()
    {
        // Soft-deleted rows must never surface implicitly; the filter is the structural guarantee.
        using var db = new SqliteTestDbContext();
        var offenders = db.Context.Model.GetEntityTypes()
            .Where(e => typeof(ISoftDeletable).IsAssignableFrom(e.ClrType))
            .Where(e => e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(offenders.Count == 0, "Soft-deletable entities without a query filter: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TenantOwnedEntities_AreDiscoverableForFutureGlobalFilters()
    {
        // Guards the invariant the (planned) global tenant filter will rely on: every tenant-owned
        // entity also carries an Id, so a generic filter/guard can be applied uniformly.
        using var db = new SqliteTestDbContext();
        var withoutId = db.Context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .Where(e => e.FindPrimaryKey() is null)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(withoutId.Count == 0, "Tenant-owned entities without a primary key: " + string.Join(", ", withoutId));
    }

    // ===================== H1 — global tenant query filter =====================

    [Fact]
    public void EveryTenantOwnedEntity_HasAQueryFilter()
    {
        // The structural guarantee itself: no tenant-owned entity may exist without the global
        // filter (a new entity is fenced automatically, or this test fails the build).
        using var db = new SqliteTestDbContext();
        var offenders = db.Context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType) && !e.IsOwned() && e.BaseType is null)
            .Where(e => e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(offenders.Count == 0, "Tenant-owned entities without a query filter: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task RequestScopedContext_OnlySeesItsOwnTenant_EvenWithoutExplicitWhere()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantB, LicensePlate = "B-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        // Deliberately NO Where(TenantId == …): the global filter is the only fence here.
        using (var contextA = db.CreateContextForTenant(tenantA))
        {
            var visible = await contextA.Vehicles.ToListAsync();
            Assert.Equal("A-1", Assert.Single(visible).LicensePlate);
        }

        using (var contextB = db.CreateContextForTenant(tenantB))
        {
            var visible = await contextB.Vehicles.ToListAsync();
            Assert.Equal("B-1", Assert.Single(visible).LicensePlate);
        }
    }

    [Fact]
    public async Task SystemScopedContext_WithoutAmbientTenant_SeesEverything()
    {
        // Background jobs, seeders and migrations run without an ambient tenant — the filter is
        // open there by design (the documented bypass).
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantB, LicensePlate = "B-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        Assert.Equal(2, await db.Context.Vehicles.CountAsync());
    }

    [Fact]
    public async Task GlobalTenantFilter_ComposesWithSoftDelete()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        var deleted = new Vehicle { Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-DEL", InternalNumber = "V-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var kept = new Vehicle { Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-KEEP", InternalNumber = "V-2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Context.Vehicles.AddRange(deleted, kept,
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantB, LicensePlate = "B-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        db.Context.Remove(deleted); // soft delete via interceptor
        await db.Context.SaveChangesAsync();

        using var contextA = db.CreateContextForTenant(tenantA);
        // Both filters apply: not soft-deleted AND own tenant.
        var visible = await contextA.Vehicles.ToListAsync();
        Assert.Equal("A-KEEP", Assert.Single(visible).LicensePlate);
    }

    // ===================== M12 / L8 — cross-tenant references =====================

    private static async Task<(SqliteTestDbContext Db, Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync()
    {
        var db = new SqliteTestDbContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        db.Context.Tenants.AddRange(
            new Tenant { Id = a, Name = "A", Slug = "a", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Tenant { Id = b, Name = "B", Slug = "b", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return (db, a, b);
    }

    [Fact]
    public async Task TenantReferenceGuard_RejectsForeignId_AndAcceptsOwnId()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        var foreignVehicle = new Vehicle
        {
            Id = Guid.NewGuid(), TenantId = tenantB, LicensePlate = "B-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var ownVehicle = new Vehicle
        {
            Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Vehicles.AddRange(foreignVehicle, ownVehicle);
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<TransportationService.Api.Common.InvalidTenantReferenceException>(() =>
            db.Context.Vehicles.EnsureBelongsToTenantAsync(foreignVehicle.Id, tenantA, "voertuig", CancellationToken.None));

        // Own id and null (optional reference) are both accepted.
        await db.Context.Vehicles.EnsureBelongsToTenantAsync(ownVehicle.Id, tenantA, "voertuig", CancellationToken.None);
        await db.Context.Vehicles.EnsureBelongsToTenantAsync(null, tenantA, "voertuig", CancellationToken.None);
    }

    [Fact]
    public async Task TenantReferenceGuard_RejectsUnknownId()
    {
        var (db, tenantA, _) = await SeedTwoTenantsAsync();
        using var _2 = db;

        await Assert.ThrowsAsync<TransportationService.Api.Common.InvalidTenantReferenceException>(() =>
            db.Context.Vehicles.EnsureBelongsToTenantAsync(Guid.NewGuid(), tenantA, "voertuig", CancellationToken.None));
    }

    [Fact]
    public async Task TenantReferenceGuard_BulkVariant_RejectsWhenAnyIdIsForeign()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        var own = new Vehicle { Id = Guid.NewGuid(), TenantId = tenantA, LicensePlate = "A-2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var foreign = new Vehicle { Id = Guid.NewGuid(), TenantId = tenantB, LicensePlate = "B-2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Context.Vehicles.AddRange(own, foreign);
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<TransportationService.Api.Common.InvalidTenantReferenceException>(() =>
            db.Context.Vehicles.EnsureAllBelongToTenantAsync([own.Id, foreign.Id], tenantA, "voertuig", CancellationToken.None));

        await db.Context.Vehicles.EnsureAllBelongToTenantAsync([own.Id], tenantA, "voertuig", CancellationToken.None);
    }

    // ===================== M1 — globally unique Peppol identity =====================

    [Fact]
    public async Task PeppolParticipantIdentity_CannotBeClaimedByASecondTenant()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantA, LegalName = "A BV",
            PeppolScheme = "0208", PeppolId = "0123456789",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        // Tenant B tries to claim tenant A's Peppol identity: inbound routing keys off this pair,
        // so the database must refuse it outright.
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantB, LegalName = "B BV",
            PeppolScheme = "0208", PeppolId = "0123456789",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task DifferentPeppolIdentities_RemainAllowedAcrossTenants()
    {
        var (db, tenantA, tenantB) = await SeedTwoTenantsAsync();
        using var _ = db;
        db.Context.LegalEntities.AddRange(
            new LegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantA, LegalName = "A BV",
                PeppolScheme = "0208", PeppolId = "0000000001",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
            new LegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantB, LegalName = "B BV",
                PeppolScheme = "0208", PeppolId = "0000000002",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });

        await db.Context.SaveChangesAsync();
        Assert.Equal(2, await db.Context.LegalEntities.CountAsync());
    }
}
