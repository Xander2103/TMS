using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Fleet;

public class FleetAssignmentServiceTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, FleetAssignmentService Sut, Guid TenantId,
        Guid VehicleA, Guid VehicleB, Guid DriverJan, Guid DriverPiet);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleA = Guid.NewGuid();
        var vehicleB = Guid.NewGuid();
        var driverJan = Guid.NewGuid();
        var driverPiet = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        var employeeJan = Guid.NewGuid();
        var employeePiet = Guid.NewGuid();
        db.Context.Employees.Add(NewEmployee(employeeJan, tenantId, "MED-1", "Jan", "Janssen"));
        db.Context.Employees.Add(NewEmployee(employeePiet, tenantId, "MED-2", "Piet", "Peeters"));
        db.Context.Drivers.Add(new Driver { Id = driverJan, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeJan, IsActive = true });
        db.Context.Drivers.Add(new Driver { Id = driverPiet, TenantId = tenantId, DriverNumber = "CH-2", EmployeeId = employeePiet, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleA, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-AAA-1", IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleB, TenantId = tenantId, InternalNumber = "VRT-2", LicensePlate = "2-BBB-2", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new FleetAssignmentService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
        return new Harness(db, sut, tenantId, vehicleA, vehicleB, driverJan, driverPiet);
    }

    private static Employee NewEmployee(Guid id, Guid tenantId, string number, string firstName, string lastName) => new()
    {
        Id = id, TenantId = tenantId, EmployeeNumber = number, FirstName = firstName, LastName = lastName,
        Street = "A", HouseNumber = "1", PostalCode = "1000", City = "B", PhoneNumber = "+32", Email = $"{number}@x.y",
        DateOfBirth = new DateOnly(1990, 1, 1), EmploymentStartDate = new DateOnly(2020, 1, 1),
        EmploymentStatus = EmploymentStatus.Active, IsActive = true,
    };

    private async Task<Vehicle> ReloadVehicle(Harness h, Guid id)
    {
        h.Db.Context.ChangeTracker.Clear();
        return await h.Db.Context.Vehicles.AsNoTracking().SingleAsync(v => v.Id == id);
    }

    [Fact]
    public async Task VehicleSide_And_DriverSide_UpdateTheSameRelationship()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Assign from the vehicle side …
        var fromVehicle = await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Success, fromVehicle.Outcome);
        Assert.Equal(h.DriverJan, (await ReloadVehicle(h, h.VehicleA)).FixedDriverId);

        // … clear from the driver side: same storage, so it must see and remove it.
        var cleared = await h.Sut.SetDriverVehicleAsync(h.DriverJan, AssignmentKind.Fixed, null, false, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Success, cleared.Outcome);
        Assert.Null((await ReloadVehicle(h, h.VehicleA)).FixedDriverId);
    }

    [Fact]
    public async Task ReassigningDriver_ToAnotherVehicle_RequiresReplace_ThenMovesAtomically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);

        // Without replaceExisting: conflict describing the current holder.
        var conflict = await h.Sut.SetVehicleDriverAsync(h.VehicleB, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Conflict, conflict.Outcome);
        Assert.Equal(h.VehicleA, conflict.Conflict!.VehicleId);
        Assert.Equal("Jan Janssen", conflict.Conflict.DriverName);

        // With replaceExisting: the old vehicle is cleared and the new one set, atomically.
        var moved = await h.Sut.SetVehicleDriverAsync(h.VehicleB, AssignmentKind.Fixed, h.DriverJan, true, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Success, moved.Outcome);
        Assert.Null((await ReloadVehicle(h, h.VehicleA)).FixedDriverId);
        Assert.Equal(h.DriverJan, (await ReloadVehicle(h, h.VehicleB)).FixedDriverId);
    }

    [Fact]
    public async Task ReplacingAVehiclesDriver_RequiresReplaceFlag()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);

        var conflict = await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverPiet, false, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Conflict, conflict.Outcome);
        Assert.Equal("Jan Janssen", conflict.Conflict!.DriverName);

        var replaced = await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverPiet, true, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Success, replaced.Outcome);
        Assert.Equal(h.DriverPiet, (await ReloadVehicle(h, h.VehicleA)).FixedDriverId);
    }

    [Fact]
    public async Task DriverSide_AssignToOccupiedVehicle_ConflictsThenReplaces()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Current, h.DriverJan, false, CancellationToken.None);

        var conflict = await h.Sut.SetDriverVehicleAsync(h.DriverPiet, AssignmentKind.Current, h.VehicleA, false, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Conflict, conflict.Outcome);

        var replaced = await h.Sut.SetDriverVehicleAsync(h.DriverPiet, AssignmentKind.Current, h.VehicleA, true, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Success, replaced.Outcome);
        Assert.Equal(h.DriverPiet, (await ReloadVehicle(h, h.VehicleA)).CurrentDriverId);
    }

    [Fact]
    public async Task FixedAndCurrentSlots_AreIndependent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);
        var current = await h.Sut.SetVehicleDriverAsync(h.VehicleB, AssignmentKind.Current, h.DriverJan, false, CancellationToken.None);

        Assert.Equal(AssignmentOutcome.Success, current.Outcome);
        Assert.Equal(h.DriverJan, (await ReloadVehicle(h, h.VehicleA)).FixedDriverId);
        Assert.Equal(h.DriverJan, (await ReloadVehicle(h, h.VehicleB)).CurrentDriverId);
    }

    [Fact]
    public async Task ForeignDriver_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignDriver = Guid.NewGuid();
        h.Db.Context.Drivers.Add(new Driver { Id = foreignDriver, TenantId = Guid.NewGuid(), DriverNumber = "CH-X", EmployeeId = Guid.NewGuid(), IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, foreignDriver, false, CancellationToken.None);

        Assert.Equal(AssignmentOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task AssignmentChanges_AreAudited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);
        await h.Sut.SetVehicleDriverAsync(h.VehicleB, AssignmentKind.Fixed, h.DriverJan, true, CancellationToken.None);

        var entries = await h.Db.Context.AuditLogs
            .Where(a => a.TenantId == h.TenantId && a.Action == "AssignmentChanged")
            .ToListAsync();
        // 1 for the initial assignment + 2 for the move (old cleared, new set).
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task UniqueIndex_BlocksDirectDuplicateFixedDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.SetVehicleDriverAsync(h.VehicleA, AssignmentKind.Fixed, h.DriverJan, false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        // Bypass the service to simulate a racing writer: the database itself must refuse.
        var vehicleB = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleB);
        vehicleB.FixedDriverId = h.DriverJan;

        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.Context.SaveChangesAsync());
    }
}
