using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class TrailerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TrailerService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TrailerNumberPrefix = "OPL-", TrailerNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TrailerService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), TimeProvider.System);
        return new Harness(db, sut, tenantId);
    }

    private static CreateTrailerRequest CreateRequest(string plate = "O-ABC-1") => new(
        plate, Vin: null, CategoryId: null, Brand: "Schmitz", Model: "Cargobull", Year: 2021,
        FirstRegistrationDate: new DateOnly(2021, 1, 1), CapacityKg: 24000m, LengthMeters: 13.6m, WidthMeters: 2.55m, HeightMeters: 2.7m, VolumeM3: 90m,
        HasRefrigeration: true, AdrSuitable: false, OwnershipType: VehicleOwnershipType.Leased, Notes: null);

    [Fact]
    public async Task Create_GeneratesInternalNumber_AndUppercasesPlate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(CreateRequest("o-abc-1"), CancellationToken.None);

        Assert.Equal(TrailerOperationOutcome.Success, result.Outcome);
        Assert.Equal("OPL-0001", result.Trailer!.InternalNumber);
        Assert.Equal("O-ABC-1", result.Trailer.LicensePlate);
        Assert.True(result.Trailer.HasRefrigeration);
    }

    [Fact]
    public async Task Create_DuplicateLicensePlate_ReturnsConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var second = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(TrailerOperationOutcome.DuplicateLicensePlate, second.Outcome);
    }

    [Fact]
    public async Task Search_DoesNotLeakOtherTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.Set<Trailer>().Add(new Trailer { Id = Guid.NewGuid(), TenantId = otherTenant, InternalNumber = "X", LicensePlate = "OTHER-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var page = await h.Sut.SearchAsync(null, null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Delete_SoftDeletes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var deleted = await h.Sut.DeleteAsync(created.Trailer!.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await h.Sut.GetByIdAsync(created.Trailer.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Update_ChangesStatusAndPersists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var result = await h.Sut.UpdateAsync(created.Trailer!.Id, new UpdateTrailerRequest(
            created.Trailer.LicensePlate, null, null, "Krone", null, null, null, null, null, null, null, null,
            HasRefrigeration: false, AdrSuitable: true, OwnershipType: VehicleOwnershipType.Owned,
            OperationalStatus: TrailerOperationalStatus.InMaintenance, IsActive: true, Notes: "In werkplaats"), CancellationToken.None);

        Assert.Equal(TrailerOperationOutcome.Success, result.Outcome);
        Assert.Equal(TrailerOperationalStatus.InMaintenance, result.Trailer!.OperationalStatus);
        Assert.True(result.Trailer.AdrSuitable);
        Assert.Equal("Krone", result.Trailer.Brand);
    }
}
