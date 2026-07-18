using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class VehicleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, VehicleService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, VehicleNumberPrefix = "VRT-", VehicleNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new VehicleService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, sut, tenantId);
    }

    private static CreateVehicleRequest CreateRequest(string plate = "1-ABC-123") => new(
        plate, Vin: "VF1ABC000000000", CategoryId: null, Brand: "Volvo", Model: "FH16", Year: 2022,
        FirstRegistrationDate: new DateOnly(2022, 1, 1), FuelType: FuelType.Diesel, EmissionClass: EmissionClass.Euro6,
        GrossVehicleWeightKg: 40000m, PayloadKg: 24000m, LengthMeters: 16.5m, WidthMeters: 2.55m, HeightMeters: 4m, VolumeM3: 90m,
        OdometerKm: 100000, HasCrane: false, HasRefrigeration: false, HasTailLift: false, AdrSuitable: true,
        OwnershipType: VehicleOwnershipType.Owned, FixedDriverId: null, CurrentDriverId: null, Notes: null);

    [Fact]
    public async Task Create_GeneratesInternalNumber_AndUppercasesPlate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(CreateRequest("1-abc-123"), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.Success, result.Outcome);
        Assert.Equal("VRT-0001", result.Vehicle!.InternalNumber);
        Assert.Equal("1-ABC-123", result.Vehicle.LicensePlate);
    }

    [Fact]
    public async Task Create_DuplicateLicensePlate_ReturnsConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var second = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.DuplicateLicensePlate, second.Outcome);
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
        h.Db.Context.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), TenantId = otherTenant, InternalNumber = "X", LicensePlate = "OTHER-1", IsActive = true });
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

        var deleted = await h.Sut.DeleteAsync(created.Vehicle!.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await h.Sut.GetByIdAsync(created.Vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Detail_ResolvesFixedDriverName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Set<Driver>().Add(new Driver { Id = driverId, TenantId = h.TenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Sut.CreateAsync(CreateRequest() with { FixedDriverId = driverId }, CancellationToken.None);

        Assert.Equal("Jan Jansen", created.Vehicle!.FixedDriverName);
    }

    [Fact]
    public async Task GetOptions_OnlyReturnsActive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);
        await h.Sut.UpdateAsync(created.Vehicle!.Id, new UpdateVehicleRequest(
            created.Vehicle.LicensePlate, null, null, null, null, null, null, FuelType.Diesel, null,
            null, null, null, null, null, null, 0, false, false, false, false,
            VehicleOwnershipType.Owned, VehicleOperationalStatus.Active, IsActive: false, null, null, null), CancellationToken.None);

        var options = await h.Sut.GetOptionsAsync(CancellationToken.None);

        Assert.Empty(options);
    }
}
