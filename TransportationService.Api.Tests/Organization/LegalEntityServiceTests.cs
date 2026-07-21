using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Dtos;
using TransportationService.Api.Modules.Organization.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Organization;

public class LegalEntityServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static LegalEntityService Build(SqliteTestDbContext db, Guid tenantId, Guid? userId = null)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId ?? UserId);
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        return new LegalEntityService(db.Context, tenant, user,
            new AuditService(db.Context, tenant, user),
            new CountryCodeValidator(db.Context),
            new LocalFileStorageService(storageRoot));
    }

    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedTenantAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        return (db, tenantId);
    }

    private static SaveLegalEntityRequest Request(string name = "Acme Transport BV", bool isDefault = false) => new(
        LegalName: name, TradingName: null, CompanyNumber: null, VatNumber: "BE0417497106",
        PeppolId: null, PeppolScheme: null,
        Street: "Havenlaan", HouseNumber: "1", PostalCode: "2000", City: "Antwerpen", CountryCode: "be",
        Email: null, PhoneNumber: null, Website: null,
        Iban: "BE68 5390 0754 7034", Bic: "bbru bebb".Replace(" ", string.Empty), BankName: "Belfius",
        DefaultCurrency: "eur", PaymentTermDays: 30,
        InvoiceNumberFormat: "{YYYY}{MM}{SEQ}", InvoiceSequencePadding: 4, InvoicePrefix: null, InvoiceFooter: null,
        IsDefault: isDefault);

    [Fact]
    public async Task Create_FirstEntity_BecomesDefault_AndNormalizes()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);

        var created = await sut.CreateAsync(Request(), CancellationToken.None);

        Assert.True(created.IsDefault);
        Assert.Equal("EUR", created.DefaultCurrency);
        Assert.Equal("BE", created.CountryCode);
        Assert.Equal("BE68539007547034", created.Iban);
    }

    [Fact]
    public async Task Create_SecondDefault_MovesDefaultFlag()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);

        var first = await sut.CreateAsync(Request("Eerste BV"), CancellationToken.None);
        var second = await sut.CreateAsync(Request("Tweede BV", isDefault: true), CancellationToken.None);

        Assert.True(second.IsDefault);
        var firstReloaded = await sut.GetAsync(first.Id, CancellationToken.None);
        Assert.False(firstReloaded!.IsDefault);
    }

    [Fact]
    public async Task Create_InvalidFormat_WithoutSeqToken_Throws()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(Request() with { InvoiceNumberFormat = "{YYYY}{MM}" }, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_DefaultEntity_IsRefused()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);
        var created = await sut.CreateAsync(Request(), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.SetActiveAsync(created.Id, false, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_NonDefault_Works_AndOptionsHideIt()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);
        await sut.CreateAsync(Request("Eerste BV"), CancellationToken.None);
        var second = await sut.CreateAsync(Request("Tweede BV"), CancellationToken.None);

        var updated = await sut.SetActiveAsync(second.Id, false, CancellationToken.None);

        Assert.False(updated!.IsActive);
        var options = await sut.ListOptionsAsync(CancellationToken.None);
        Assert.DoesNotContain(options, o => o.Id == second.Id);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantEntity_IsInvisible()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var otherTenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var mine = Build(db, tenantId);
        var theirs = Build(db, otherTenantId);
        var created = await theirs.CreateAsync(Request("Andermans BV"), CancellationToken.None);

        Assert.Null(await mine.GetAsync(created.Id, CancellationToken.None));
        Assert.Empty(await mine.ListAsync(includeInactive: true, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveSelection_DefaultsToDefaultEntity_AndValidatesTenantAndActive()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);
        var first = await sut.CreateAsync(Request("Eerste BV"), CancellationToken.None);
        var second = await sut.CreateAsync(Request("Tweede BV"), CancellationToken.None);

        // No explicit selection yet: fall back to the default entity.
        var initial = await sut.GetActiveSelectionAsync(CancellationToken.None);
        Assert.Equal(first.Id, initial.LegalEntityId);

        var set = await sut.SetActiveSelectionAsync(second.Id, CancellationToken.None);
        Assert.Equal(second.Id, set.LegalEntityId);
        Assert.Equal(second.Id, (await sut.GetActiveSelectionAsync(CancellationToken.None)).LegalEntityId);

        // Unknown/foreign entity id is refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.SetActiveSelectionAsync(Guid.NewGuid(), CancellationToken.None));

        // Deactivating the selected entity makes the selection fall back to the default.
        await sut.SetActiveAsync(second.Id, false, CancellationToken.None);
        var afterDeactivate = await sut.GetActiveSelectionAsync(CancellationToken.None);
        Assert.Equal(first.Id, afterDeactivate.LegalEntityId);
    }

    [Fact]
    public async Task Seeder_CreatesDefaultEntityFromTenantSettings_Idempotently()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CompanyLegalName = "Acme Transport BV",
            VatNumber = "BE0417497106",
            InvoiceNumberPrefix = "FAC-",
        });
        await db.Context.SaveChangesAsync();

        await LegalEntitySeeder.SeedAsync(db.Context);
        await LegalEntitySeeder.SeedAsync(db.Context);

        var entities = await db.Context.LegalEntities.Where(e => e.TenantId == tenantId).ToListAsync();
        var entity = Assert.Single(entities);
        Assert.True(entity.IsDefault);
        Assert.Equal("Acme Transport BV", entity.LegalName);
        Assert.Equal("FAC-", entity.InvoicePrefix);
        Assert.Equal("{PREFIX}{YYYY}{MM}{SEQ}", entity.InvoiceNumberFormat);
    }

    [Fact]
    public async Task Logo_UploadDownloadRemove_Roundtrip()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);
        var created = await sut.CreateAsync(Request(), CancellationToken.None);

        using var upload = new MemoryStream([1, 2, 3, 4]);
        var withLogo = await sut.AttachLogoAsync(created.Id, "logo.png", "image/png", upload, CancellationToken.None);
        Assert.True(withLogo!.HasLogo);

        var opened = await sut.OpenLogoAsync(created.Id, CancellationToken.None);
        Assert.NotNull(opened);
        using var ms = new MemoryStream();
        await opened!.Value.Content.CopyToAsync(ms);
        Assert.Equal(4, ms.Length);
        await opened.Value.Content.DisposeAsync();

        var removed = await sut.RemoveLogoAsync(created.Id, CancellationToken.None);
        Assert.False(removed!.HasLogo);
        Assert.Null(await sut.OpenLogoAsync(created.Id, CancellationToken.None));
    }
}
