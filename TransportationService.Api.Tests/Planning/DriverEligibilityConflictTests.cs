using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Planning;

public class DriverEligibilityConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, PlanningConflictService Sut, Guid TenantId, Guid VehicleId, Guid DriverId, Guid EmployeeId);

    private static async Task<Harness> SeedAsync(string? requiredLicence)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            OperationalStatus = VehicleOperationalStatus.Available, IsActive = true, RequiredLicenceCode = requiredLicence,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "j@a.be", PhoneNumber = "1", Street = "S", HouseNumber = "1",
            PostalCode = "2000", City = "A", EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, EmployeeId = employeeId, DriverNumber = "CH-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var sut = new PlanningConflictService(db.Context, new DevTenantContext(tenantId),
            new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, driverId, employeeId);
    }

    private static async Task GiveLicenceAsync(Harness h, string typeCode, DateOnly? expiry)
    {
        var typeId = Guid.NewGuid();
        h.Db.Context.QualificationTypes.Add(new QualificationType { Id = typeId, Code = typeCode, Name = typeCode, Category = "Rijbewijs", RequiresExpiryDate = true, IsActive = true });
        h.Db.Context.EmployeeQualifications.Add(new EmployeeQualification
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId, QualificationTypeId = typeId,
            ObtainedDate = new(2020, 1, 1), ExpiryDate = expiry, Status = QualificationStatus.Valid,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    private static Trip TripFor(Harness h) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1",
        TripDate = new DateOnly(2026, 7, 20), VehicleId = h.VehicleId, DriverId = h.DriverId, Status = TripStatus.Draft,
    };

    [Fact]
    public async Task CeVehicle_WithOnlyCLicence_IsBlocking()
    {
        var h = await SeedAsync("CE");
        using var _ = h.Db;
        await GiveLicenceAsync(h, QualificationTypeCodes.DrivingLicenceC, new DateOnly(2030, 1, 1));

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.DriverLicenceInsufficient);
        Assert.True(conflict.Blocking);
        Assert.True(conflict.OverrideAllowed);
    }

    [Fact]
    public async Task CeVehicle_WithValidCeLicence_IsAllowed()
    {
        var h = await SeedAsync("CE");
        using var _ = h.Db;
        await GiveLicenceAsync(h, QualificationTypeCodes.DrivingLicenceCE, new DateOnly(2030, 1, 1));

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.DriverLicenceInsufficient);
    }

    [Fact]
    public async Task CeVehicle_WithExpiredCeLicence_IsBlocking()
    {
        var h = await SeedAsync("CE");
        using var _ = h.Db;
        await GiveLicenceAsync(h, QualificationTypeCodes.DrivingLicenceCE, new DateOnly(2026, 1, 1)); // expired

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.Contains(conflicts, c => c.Code == PlanningConflictCode.DriverLicenceInsufficient && c.Blocking);
    }

    [Fact]
    public async Task VehicleWithoutRequiredLicence_ProducesNoLicenceConflict()
    {
        var h = await SeedAsync(requiredLicence: null);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.DriverLicenceInsufficient);
    }

    [Fact]
    public async Task OverdueTachograph_IsAnOverrideableWarning()
    {
        var h = await SeedAsync("C");
        using var _ = h.Db;
        await GiveLicenceAsync(h, QualificationTypeCodes.DrivingLicenceC, new DateOnly(2030, 1, 1));
        h.Db.Context.TachographCalibrations.Add(new TachographCalibration
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            CalibrationDate = new DateOnly(2024, 1, 1), NextCalibrationDue = new DateOnly(2026, 1, 1), // overdue
        });
        await h.Db.Context.SaveChangesAsync();

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.TachographOverdue);
        Assert.False(conflict.Blocking);
    }
}
