using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class FleetAssignmentService : IFleetAssignmentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public FleetAssignmentService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<AssignmentResult> SetVehicleDriverAsync(
        Guid vehicleId, AssignmentKind kind, Guid? driverId, bool replaceExisting, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var vehicle = await _dbContext.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.TenantId == tenantId, cancellationToken);
        if (vehicle is null)
        {
            return AssignmentResult.NotFound;
        }

        if (driverId is { } requestedDriver
            && !await _dbContext.Drivers.AnyAsync(d => d.Id == requestedDriver && d.TenantId == tenantId, cancellationToken))
        {
            return AssignmentResult.InvalidReference;
        }

        return await ApplyAsync(vehicle, kind, driverId, replaceExisting, cancellationToken);
    }

    public async Task<AssignmentResult> SetDriverVehicleAsync(
        Guid driverId, AssignmentKind kind, Guid? vehicleId, bool replaceExisting, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (!await _dbContext.Drivers.AnyAsync(d => d.Id == driverId && d.TenantId == tenantId, cancellationToken))
        {
            return AssignmentResult.NotFound;
        }

        if (vehicleId is { } requestedVehicle)
        {
            var vehicle = await _dbContext.Vehicles
                .FirstOrDefaultAsync(v => v.Id == requestedVehicle && v.TenantId == tenantId, cancellationToken);
            if (vehicle is null)
            {
                return AssignmentResult.InvalidReference;
            }

            // Driver-side "set my vehicle" also has to respect the target vehicle's occupant.
            var occupant = GetSlot(vehicle, kind);
            if (occupant is { } otherDriver && otherDriver != driverId && !replaceExisting)
            {
                return AssignmentResult.ConflictWith(await DescribeAsync(vehicle, otherDriver, cancellationToken));
            }

            return await ApplyAsync(vehicle, kind, driverId, replaceExisting, cancellationToken);
        }

        // Clearing from the driver side: find whichever vehicle currently holds this driver.
        var holder = await FindHolderAsync(kind, driverId, excludeVehicleId: null, cancellationToken);
        if (holder is null)
        {
            return AssignmentResult.Success; // Nothing to clear.
        }

        return await ApplyAsync(holder, kind, null, replaceExisting: true, cancellationToken);
    }

    /// <summary>The vehicle (if any) holding this driver in the given slot; EF-translatable per kind.</summary>
    private Task<Vehicle?> FindHolderAsync(AssignmentKind kind, Guid driverId, Guid? excludeVehicleId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.Vehicles.Where(v => v.TenantId == tenantId);
        if (excludeVehicleId is { } exclude)
        {
            query = query.Where(v => v.Id != exclude);
        }

        return kind == AssignmentKind.Fixed
            ? query.FirstOrDefaultAsync(v => v.FixedDriverId == driverId, cancellationToken)
            : query.FirstOrDefaultAsync(v => v.CurrentDriverId == driverId, cancellationToken);
    }

    /// <summary>
    /// Core mutation: puts <paramref name="driverId"/> in the given slot of <paramref name="vehicle"/>,
    /// atomically clearing (a) the vehicle's previous occupant implicitly and (b) any OTHER vehicle
    /// currently holding this driver in the same slot — a driver holds at most one vehicle per slot,
    /// which the filtered unique index also enforces as a backstop against races.
    /// </summary>
    private async Task<AssignmentResult> ApplyAsync(
        Vehicle vehicle, AssignmentKind kind, Guid? driverId, bool replaceExisting, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var previousDriver = GetSlot(vehicle, kind);
        if (previousDriver == driverId)
        {
            return AssignmentResult.Success; // No-op.
        }

        if (previousDriver is { } displaced && driverId is not null && !replaceExisting)
        {
            return AssignmentResult.ConflictWith(await DescribeAsync(vehicle, displaced, cancellationToken));
        }

        Vehicle? otherHolder = null;
        if (driverId is { } newDriver)
        {
            otherHolder = await FindHolderAsync(kind, newDriver, excludeVehicleId: vehicle.Id, cancellationToken);
            if (otherHolder is not null && !replaceExisting)
            {
                return AssignmentResult.ConflictWith(await DescribeAsync(otherHolder, newDriver, cancellationToken));
            }
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (otherHolder is not null)
            {
                SetSlot(otherHolder, kind, null);
                await RecordAsync(otherHolder, kind, driverId, null, cancellationToken);
            }

            SetSlot(vehicle, kind, driverId);
            await RecordAsync(vehicle, kind, previousDriver, driverId, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            // The filtered unique index caught a concurrent assignment of the same driver;
            // disposing the transaction rolls everything back.
            return driverId is { } concurrentDriver
                ? AssignmentResult.ConflictWith(await DescribeAsync(vehicle, concurrentDriver, cancellationToken))
                : AssignmentResult.InvalidReference;
        }

        await transaction.CommitAsync(cancellationToken);
        return AssignmentResult.Success;
    }

    private static Guid? GetSlot(Vehicle vehicle, AssignmentKind kind) =>
        kind == AssignmentKind.Fixed ? vehicle.FixedDriverId : vehicle.CurrentDriverId;

    private static void SetSlot(Vehicle vehicle, AssignmentKind kind, Guid? driverId)
    {
        if (kind == AssignmentKind.Fixed)
        {
            vehicle.FixedDriverId = driverId;
        }
        else
        {
            vehicle.CurrentDriverId = driverId;
        }
    }

    private async Task<AssignmentConflictDto> DescribeAsync(Vehicle vehicle, Guid driverId, CancellationToken cancellationToken)
    {
        var driverName = await (from d in _dbContext.Drivers.AsNoTracking()
                                join e in _dbContext.Employees.AsNoTracking() on d.EmployeeId equals e.Id
                                where d.Id == driverId && d.TenantId == _tenantContext.TenantId
                                select e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Onbekende chauffeur";

        return new AssignmentConflictDto(vehicle.Id, $"{vehicle.InternalNumber} · {vehicle.LicensePlate}", driverId, driverName);
    }

    private Task RecordAsync(Vehicle vehicle, AssignmentKind kind, Guid? oldDriverId, Guid? newDriverId, CancellationToken cancellationToken) =>
        _auditService.RecordAsync("Vehicle", vehicle.Id.ToString(), "AssignmentChanged",
            new { Kind = kind.ToString(), DriverId = oldDriverId },
            new { Kind = kind.ToString(), DriverId = newDriverId },
            cancellationToken);
}
