using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>D4: <c>Vehicle.TailLiftCapacityKg</c> — nullable, ≥ 0, only meaningful with a tail lift.</summary>
public class VehicleTailLiftCapacityTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(SqliteTestDbContext Db, VehicleService Sut)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, VehicleNumberPrefix = "VRT-", VehicleNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        return (db, new VehicleService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), TimeProvider.System));
    }

    private static CreateVehicleRequest Create(bool hasTailLift, decimal? capacityKg, string plate = "1-ABC-123") => new(
        plate, Vin: null, CategoryId: null, Brand: "Volvo", Model: "FL", Year: 2022,
        FirstRegistrationDate: null, FuelType: FuelType.Diesel, EmissionClass: null,
        GrossVehicleWeightKg: 18000m, PayloadKg: 9000m, LengthMeters: null, WidthMeters: null, HeightMeters: null, VolumeM3: null,
        OdometerKm: 0, ConsumptionLPer100Km: null, HasCrane: false, HasRefrigeration: false, HasTailLift: hasTailLift, AdrSuitable: false,
        OwnershipType: VehicleOwnershipType.Owned, FixedDriverId: null, CurrentDriverId: null, Notes: null,
        TailLiftCapacityKg: capacityKg);

    private static UpdateVehicleRequest Update(VehicleDetailDto v, bool hasTailLift, decimal? capacityKg) => new(
        v.LicensePlate, v.Vin, v.CategoryId, v.Brand, v.Model, v.Year, v.FirstRegistrationDate, v.FuelType, v.EmissionClass,
        v.GrossVehicleWeightKg, v.PayloadKg, v.LengthMeters, v.WidthMeters, v.HeightMeters, v.VolumeM3,
        v.OdometerKm, v.ConsumptionLPer100Km, v.HasCrane, v.HasRefrigeration, hasTailLift, v.AdrSuitable,
        v.OwnershipType, v.OperationalStatus, v.IsActive, v.Notes,
        TailLiftCapacityKg: capacityKg);

    [Fact]
    public async Task Create_StoresTheCapacity_AndReturnsItOnTheDetail()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var result = await sut.CreateAsync(Create(hasTailLift: true, capacityKg: 1500.5m), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.Success, result.Outcome);
        Assert.Equal(1500.5m, result.Vehicle!.TailLiftCapacityKg);
        Assert.Equal(1500.5m, (await sut.GetByIdAsync(result.Vehicle.Id, CancellationToken.None))!.TailLiftCapacityKg);
    }

    [Fact]
    public async Task UnknownCapacity_StaysNull_NothingIsInvented()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var result = await sut.CreateAsync(Create(hasTailLift: true, capacityKg: null), CancellationToken.None);

        Assert.True(result.Vehicle!.HasTailLift);
        Assert.Null(result.Vehicle.TailLiftCapacityKg);
    }

    [Fact]
    public async Task WithoutATailLift_TheCapacityIsNotStored()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var result = await sut.CreateAsync(Create(hasTailLift: false, capacityKg: 1500m), CancellationToken.None);

        Assert.Null(result.Vehicle!.TailLiftCapacityKg);
        Assert.Null((await db.Context.Vehicles.AsNoTracking().SingleAsync()).TailLiftCapacityKg);
    }

    [Fact]
    public async Task NegativeCapacity_IsAFieldError_OnCreateAndUpdate()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var onCreate = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(Create(hasTailLift: true, capacityKg: -1m), CancellationToken.None));
        Assert.True(onCreate.FieldErrors!.ContainsKey("tailLiftCapacityKg"));

        var vehicle = (await sut.CreateAsync(Create(hasTailLift: true, capacityKg: 1000m), CancellationToken.None)).Vehicle!;
        var onUpdate = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.UpdateAsync(vehicle.Id, Update(vehicle, hasTailLift: true, capacityKg: -5m), CancellationToken.None));
        Assert.True(onUpdate.FieldErrors!.ContainsKey("tailLiftCapacityKg"));
    }

    [Fact]
    public async Task Update_ChangesTheCapacity_AndZeroIsAllowed()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;
        var vehicle = (await sut.CreateAsync(Create(hasTailLift: true, capacityKg: 1000m), CancellationToken.None)).Vehicle!;

        var raised = await sut.UpdateAsync(vehicle.Id, Update(vehicle, hasTailLift: true, capacityKg: 2000m), CancellationToken.None);
        var zero = await sut.UpdateAsync(vehicle.Id, Update(vehicle, hasTailLift: true, capacityKg: 0m), CancellationToken.None);

        Assert.Equal(2000m, raised.Vehicle!.TailLiftCapacityKg);
        Assert.Equal(0m, zero.Vehicle!.TailLiftCapacityKg);
    }
}
