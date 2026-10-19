using Microsoft.Extensions.DependencyInjection;
using TransportationService.Api.Common.Storage;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.SystemAdmin.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Storage;

/// <summary>
/// The offline integrity tool (--verify-storage): DB file references vs. the storage root. Keys
/// only, all tenants, soft-deleted rows excluded, non-zero exit when anything is missing.
/// </summary>
public class StorageIntegrityVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tms-integrity-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static async Task<Guid> SeedTenantAsync(SqliteTestDbContext db)
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = $"t-{tenantId:N}", CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    private async Task SeedLogosAsync(SqliteTestDbContext db)
    {
        // Two tenants: the scan must cover both (no ambient tenant filter in operator tooling).
        var tenantA = await SeedTenantAsync(db);
        var tenantB = await SeedTenantAsync(db);
        Directory.CreateDirectory(Path.Combine(_root, $"tenant-{tenantA}", "legal-entities"));
        await File.WriteAllTextAsync(Path.Combine(_root, $"tenant-{tenantA}", "legal-entities", "logo.png"), "png");

        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantA, LegalName = "A", IsActive = true,
                LogoStorageKey = $"tenant-{tenantA}/legal-entities/logo.png" },
            new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantB, LegalName = "B", IsActive = true,
                LogoStorageKey = $"tenant-{tenantB}/legal-entities/gone.png" },
            new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantB, LegalName = "C", IsActive = true,
                LogoStorageKey = "../../etc/passwd" },
            new LegalEntity { Id = Guid.NewGuid(), TenantId = tenantB, LegalName = "D", IsActive = true });
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public void Columns_CoverAllTwentyAuditedReferenceColumns()
    {
        Assert.Equal(20, StorageIntegrityVerifier.Columns.Count);
        Assert.Equal(20, StorageIntegrityVerifier.Columns.Select(c => c.Label).Distinct().Count());
    }

    [Fact]
    public async Task Scan_CountsPresentMissingAndInvalid_PerColumn_AcrossTenants()
    {
        using var db = new SqliteTestDbContext();
        await SeedLogosAsync(db);

        var report = await StorageIntegrityVerifier.ScanAsync(db.Context, _root, collectMissingKeys: true, CancellationToken.None);

        var logos = report.Columns.Single(c => c.Label == "legal_entities.LogoStorageKey");
        Assert.Equal(3, logos.References);
        Assert.Equal(1, logos.Present);
        Assert.Equal(1, logos.Missing);
        Assert.Equal(1, logos.Invalid);
        Assert.Single(logos.MissingKeys);
        Assert.EndsWith("gone.png", logos.MissingKeys[0]);
        // Every other column was queried (the whole model scans on SQLite too) and is empty.
        Assert.All(report.Columns.Where(c => c != logos), c => Assert.Equal(0, c.References));
    }

    [Fact]
    public async Task Run_PrintsCountsOnly_AndExitsNonZeroWhenFilesAreMissing()
    {
        using var db = new SqliteTestDbContext();
        await SeedLogosAsync(db);
        var services = new ServiceCollection()
            .AddSingleton(new StorageRoot(_root, StorageRootSource.Configured))
            .AddSingleton<TransportationDbContext>(db.Context)
            .BuildServiceProvider();
        var output = new StringWriter();

        var exitCode = await StorageIntegrityVerifier.RunAsync(services, output, listMissing: false, CancellationToken.None);

        Assert.Equal(2, exitCode);
        var text = output.ToString();
        Assert.Contains("legal_entities.LogoStorageKey", text);
        Assert.Contains("TOTAL: 3 references, 1 missing, 1 invalid.", text);
        Assert.DoesNotContain("gone.png", text); // keys only with --list-missing
    }

    [Fact]
    public async Task Run_AllPresent_ExitsZero()
    {
        using var db = new SqliteTestDbContext();
        var services = new ServiceCollection()
            .AddSingleton(new StorageRoot(_root, StorageRootSource.Configured))
            .AddSingleton<TransportationDbContext>(db.Context)
            .BuildServiceProvider();

        Assert.Equal(0, await StorageIntegrityVerifier.RunAsync(services, new StringWriter(), true, CancellationToken.None));
    }
}
