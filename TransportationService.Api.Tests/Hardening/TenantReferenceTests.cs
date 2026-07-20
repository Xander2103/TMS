using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// Cross-tenant reference hardening: writing a reference (category, driver, vehicle, trailer,
/// customer, employee, role) that belongs to another tenant must be rejected, and read-side
/// resolution must never leak another tenant's data even if such a reference exists in the DB.
/// </summary>
public class TenantReferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid ForeignTenantId);

    private static async Task<Harness> SeedTwoTenantsAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Mine", Slug = "mine", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, foreignTenantId);
    }

    private static AuditService Audit(SqliteTestDbContext db, Guid tenantId) =>
        new(db.Context, new DevTenantContext(tenantId), new DevCurrentUserContext(null));

    [Fact]
    public async Task VehicleCreate_WithForeignDriver_ReturnsInvalidReference()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var foreignEmployee = Guid.NewGuid();
        var foreignDriver = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = foreignEmployee, TenantId = h.ForeignTenantId, EmployeeNumber = "X", FirstName = "F", LastName = "D", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Drivers.Add(new Driver { Id = foreignDriver, TenantId = h.ForeignTenantId, DriverNumber = "CH-X", EmployeeId = foreignEmployee, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var sut = new VehicleService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), TimeProvider.System);
        var result = await sut.CreateAsync(new CreateVehicleRequest(
            "1-AAA-1", null, null, null, null, null, null, FuelType.Diesel, null,
            null, null, null, null, null, null, 0, null, false, false, false, false,
            VehicleOwnershipType.Owned, FixedDriverId: foreignDriver, CurrentDriverId: null, Notes: null), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task DriverCreate_WithForeignTrailer_ReturnsInvalidReference()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var employeeId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "A", LastName = "B", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        var foreignTrailer = Guid.NewGuid();
        h.Db.Context.Trailers.Add(new Trailer { Id = foreignTrailer, TenantId = h.ForeignTenantId, InternalNumber = "OPL-X", LicensePlate = "X-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var sut = new DriverService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId),
            new QualificationStatusCalculator(), new TestClock(Now));
        var result = await sut.CreateAsync(new CreateDriverRequest(
            employeeId, null, DriverAvailabilityStatus.Available,
            FixedTrailerId: foreignTrailer, Notes: null), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task LocationCreate_WithForeignCustomer_ReturnsInvalidReference()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var foreignCustomer = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = foreignCustomer, TenantId = h.ForeignTenantId, CustomerNumber = "KL-X", Name = "Foreign BV", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var sut = new LocationService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), new CountryCodeValidator(h.Db.Context));
        var result = await sut.CreateAsync(new CreateLocationRequest(
            "LOC-1", "Test", LocationType.CustomerLocation,
            null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, false, false,
            CustomerId: foreignCustomer, Notes: null), CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task VehicleDetail_WithForeignDriverRow_DoesNotLeakDriverName()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        // Simulate pre-existing bad data: a vehicle in OUR tenant pointing at a FOREIGN driver.
        var foreignEmployee = Guid.NewGuid();
        var foreignDriver = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = foreignEmployee, TenantId = h.ForeignTenantId, EmployeeNumber = "X", FirstName = "Geheim", LastName = "Persoon", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Drivers.Add(new Driver { Id = foreignDriver, TenantId = h.ForeignTenantId, DriverNumber = "CH-X", EmployeeId = foreignEmployee, IsActive = true });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = h.TenantId, InternalNumber = "VRT-1", LicensePlate = "1-B-2", CurrentDriverId = foreignDriver, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var sut = new VehicleService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), TimeProvider.System);
        var detail = await sut.GetByIdAsync(vehicleId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail!.CurrentDriverName);
    }

    [Fact]
    public async Task CustomerCreate_WithForeignCategory_Throws()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var foreignCategory = Guid.NewGuid();
        h.Db.Context.CustomerCategories.Add(new CustomerCategory { Id = foreignCategory, TenantId = h.ForeignTenantId, Code = "X", Name = "Foreign", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var sut = new CustomerService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), new CountryCodeValidator(h.Db.Context));
        var request = new CreateCustomerRequest(
            "Test BV", null, null, CategoryId: foreignCategory,
            null, null, null, null, null, null, null, null, null, 30, null, null);

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task UserCreate_WithForeignRole_Throws()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var foreignRole = Guid.NewGuid();
        h.Db.Context.Roles.Add(new Role { Id = foreignRole, TenantId = h.ForeignTenantId, Name = "ForeignAdmin", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();

        var sut = new UserService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), new TransportationService.Api.Modules.Authentication.Services.PasswordHasher());
        var request = new CreateUserRequest("a@b.com", "A", "B", null, null, new[] { foreignRole });

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task VehicleSearch_IsCaseInsensitive()
    {
        var h = await SeedTwoTenantsAsync();
        using var _ = h.Db;

        var sut = new VehicleService(h.Db.Context, new DevTenantContext(h.TenantId), Audit(h.Db, h.TenantId), TimeProvider.System);
        await sut.CreateAsync(new CreateVehicleRequest(
            "1-CCC-3", null, null, "Volvo", "FH16", null, null, FuelType.Diesel, null,
            null, null, null, null, null, null, 0, null, false, false, false, false,
            VehicleOwnershipType.Owned, null, null, null), CancellationToken.None);

        var page = await sut.SearchAsync("volvo", null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
    }
}
