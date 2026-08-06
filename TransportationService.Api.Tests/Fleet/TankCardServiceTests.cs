using TransportationService.Api.Common;
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

public class TankCardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(SqliteTestDbContext Db, TankCardService Sut, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TankCardService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId);
    }

    private static Employee NewEmployee(Guid tenantId, string number = "P-1", string firstName = "Jan", string lastName = "Peeters") => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = number, FirstName = firstName, LastName = lastName,
        CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    };

    private static Driver NewDriver(Guid tenantId, Guid employeeId, string number = "CH-1") => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, DriverNumber = number, EmployeeId = employeeId, IsActive = true,
    };

    private static CreateTankCardRequest Request(
        string cardNumber = "7002-1111-2222-0001", Guid? vehicleId = null, Guid? driverId = null, Guid? employeeId = null,
        decimal? dailyLimit = null, decimal? weeklyLimit = null, decimal? monthlyLimit = null, string? internalName = null) =>
        new(cardNumber, "DKV", vehicleId, driverId, employeeId, new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1),
            internalName, null, dailyLimit, weeklyLimit, monthlyLimit, null, null);

    [Fact]
    public void ComputeStatus_CoversLifecycle()
    {
        Assert.Equal(TankCardStatus.Active, TankCardService.ComputeStatus(false, null, Today));
        Assert.Equal(TankCardStatus.Active, TankCardService.ComputeStatus(false, Today.AddDays(61), Today));
        Assert.Equal(TankCardStatus.ExpiringSoon, TankCardService.ComputeStatus(false, Today.AddDays(60), Today));
        Assert.Equal(TankCardStatus.Expired, TankCardService.ComputeStatus(false, Today.AddDays(-1), Today));
        // Blocked wins even when the card is also expired.
        Assert.Equal(TankCardStatus.Blocked, TankCardService.ComputeStatus(true, Today.AddDays(-1), Today));
    }

    [Fact]
    public async Task Create_WithVehicle_ResolvesVehicleInfo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(vehicleId: h.VehicleId), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.Success, result.Outcome);
        Assert.Equal("VRT-0001", result.Card!.VehicleInternalNumber);
        Assert.Equal(TankCardStatus.Active, result.Card.Status);
    }

    [Fact]
    public async Task Create_DuplicateCardNumber_Conflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(), CancellationToken.None);

        var duplicate = await h.Sut.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.DuplicateCardNumber, duplicate.Outcome);
    }

    [Fact]
    public async Task Create_ForeignVehicle_ReturnsInvalidReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(vehicleId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task Create_ValidUntilBeforeValidFrom_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            new CreateTankCardRequest("X-1", "Shell", null, null, null, new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1),
                null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task SetBlocked_TogglesStatusAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(), CancellationToken.None);

        var blocked = await h.Sut.SetBlockedAsync(created.Card!.Id,
            new SetTankCardBlockedRequest(true, "Kaart verloren"), CancellationToken.None);
        Assert.Equal(TankCardStatus.Blocked, blocked.Card!.Status);
        Assert.Equal("Kaart verloren", blocked.Card.BlockedReason);

        var unblocked = await h.Sut.SetBlockedAsync(created.Card.Id,
            new SetTankCardBlockedRequest(false, null), CancellationToken.None);
        Assert.Equal(TankCardStatus.Active, unblocked.Card!.Status);
        Assert.Null(unblocked.Card.BlockedReason);
    }

    [Fact]
    public async Task Search_FiltersByComputedStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("ACTIVE-1"), CancellationToken.None);
        await h.Sut.CreateAsync(
            new CreateTankCardRequest("EXPIRED-1", "DKV", null, null, null, null, Today.AddDays(-1), null, null, null, null, null, null, null),
            CancellationToken.None);

        var expired = await h.Sut.SearchAsync(null, TankCardStatus.Expired, false, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, expired.TotalCount);
        Assert.Equal("EXPIRED-1", expired.Items[0].CardNumber);
    }

    [Fact]
    public async Task Search_MatchesVehicleNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("CARD-A", h.VehicleId), CancellationToken.None);
        await h.Sut.CreateAsync(Request("CARD-B"), CancellationToken.None);

        var byVehicle = await h.Sut.SearchAsync("vrt-0001", null, false, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, byVehicle.TotalCount);
        Assert.Equal("CARD-A", byVehicle.Items[0].CardNumber);
    }

    [Fact]
    public async Task Search_MatchesInternalName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("CARD-A", internalName: "Reservewagen 3"), CancellationToken.None);
        await h.Sut.CreateAsync(Request("CARD-B", internalName: "Hoofdwagen"), CancellationToken.None);

        var byInternalName = await h.Sut.SearchAsync("reserve", null, false, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, byInternalName.TotalCount);
        Assert.Equal("CARD-A", byInternalName.Items[0].CardNumber);
    }

    [Fact]
    public async Task Update_ToExistingCardNumber_Conflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);
        var second = await h.Sut.CreateAsync(Request("CARD-B"), CancellationToken.None);

        var result = await h.Sut.UpdateAsync(second.Card!.Id,
            new UpdateTankCardRequest("CARD-A", "DKV", null, null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.DuplicateCardNumber, result.Outcome);
    }

    [Fact]
    public async Task Delete_SoftDeletes_AndFreesCardNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(created.Card!.Id, CancellationToken.None));
        Assert.Null(await h.Sut.GetByIdAsync(created.Card.Id, CancellationToken.None));

        // Filtered unique index: the number can be reused after soft delete.
        var recreated = await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);
        Assert.Equal(TankCardOperationOutcome.Success, recreated.Outcome);
    }

    [Fact]
    public async Task Create_WithEmployeeOfDriverProfile_SyncsDriverId()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = NewEmployee(h.TenantId);
        var driver = NewDriver(h.TenantId, employee.Id);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Set<Driver>().Add(driver);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateAsync(Request(employeeId: employee.Id), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.Success, result.Outcome);
        Assert.Equal(employee.Id, result.Card!.EmployeeId);
        Assert.Equal(driver.Id, result.Card.DriverId);
        Assert.Equal("Jan Peeters", result.Card.EmployeeName);
        Assert.Equal("Jan Peeters", result.Card.DriverName);
    }

    [Fact]
    public async Task Create_WithEmployeeWithoutDriverProfile_DriverIdIsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = NewEmployee(h.TenantId);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateAsync(Request(employeeId: employee.Id), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.Success, result.Outcome);
        Assert.Equal(employee.Id, result.Card!.EmployeeId);
        Assert.Null(result.Card.DriverId);
        Assert.Null(result.Card.DriverName);
    }

    [Fact]
    public async Task Create_LegacyDriverIdOnly_ResolvesEmployeeId()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = NewEmployee(h.TenantId);
        var driver = NewDriver(h.TenantId, employee.Id);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Set<Driver>().Add(driver);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateAsync(Request(driverId: driver.Id), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.Success, result.Outcome);
        Assert.Equal(employee.Id, result.Card!.EmployeeId);
        Assert.Equal(driver.Id, result.Card.DriverId);
    }

    [Fact]
    public async Task Create_CrossTenantEmployee_ThrowsInvalidTenantReferenceException()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        var foreignEmployee = NewEmployee(otherTenantId);
        h.Db.Context.Employees.Add(foreignEmployee);
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => h.Sut.CreateAsync(Request(employeeId: foreignEmployee.Id), CancellationToken.None));
    }

    [Theory]
    [InlineData(-1.0, null, null)]
    [InlineData(null, -0.01, null)]
    [InlineData(null, null, -100.0)]
    public async Task Create_NegativeLimit_ThrowsDomainValidationException(double? daily, double? weekly, double? monthly)
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(
                Request(dailyLimit: (decimal?)daily, weeklyLimit: (decimal?)weekly, monthlyLimit: (decimal?)monthly),
                CancellationToken.None));

        Assert.NotNull(exception.FieldErrors);
        Assert.Single(exception.FieldErrors!);
    }

    [Fact]
    public async Task ListForEmployeeAsync_FiltersToThatEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee1 = NewEmployee(h.TenantId, "P-1");
        var employee2 = NewEmployee(h.TenantId, "P-2", "An", "Vermeulen");
        h.Db.Context.Employees.Add(employee1);
        h.Db.Context.Employees.Add(employee2);
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.CreateAsync(Request("CARD-1", employeeId: employee1.Id), CancellationToken.None);
        await h.Sut.CreateAsync(Request("CARD-2", employeeId: employee2.Id), CancellationToken.None);
        await h.Sut.CreateAsync(Request("CARD-3"), CancellationToken.None);

        var cards = await h.Sut.ListForEmployeeAsync(employee1.Id, CancellationToken.None);

        Assert.Single(cards);
        Assert.Equal("CARD-1", cards[0].CardNumber);
    }

    [Fact]
    public async Task Search_Available_ExcludesAssignedBlockedAndExpired()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = NewEmployee(h.TenantId);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.CreateAsync(Request("FREE-1"), CancellationToken.None);
        await h.Sut.CreateAsync(Request("ASSIGNED-1", employeeId: employee.Id), CancellationToken.None);
        var blocked = await h.Sut.CreateAsync(Request("BLOCKED-1"), CancellationToken.None);
        await h.Sut.SetBlockedAsync(blocked.Card!.Id, new SetTankCardBlockedRequest(true, "Kwijt"), CancellationToken.None);
        await h.Sut.CreateAsync(
            new CreateTankCardRequest("EXPIRED-1", "DKV", null, null, null, null, Today.AddDays(-1), null, null, null, null, null, null, null),
            CancellationToken.None);

        var available = await h.Sut.SearchAsync(null, null, true, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, available.TotalCount);
        Assert.Equal("FREE-1", available.Items[0].CardNumber);
    }
}
