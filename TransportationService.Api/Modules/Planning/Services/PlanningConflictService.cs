using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public class PlanningConflictService : IPlanningConflictService
{
    private const int DefaultExpiryWarningDays = 30;

    /// <summary>Trip statuses that occupy a resource; drafts and cancelled trips never block others.</summary>
    private static readonly TripStatus[] OccupyingStatuses = [TripStatus.Planned, TripStatus.InProgress];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;

    public PlanningConflictService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PlanningConflictDto>> EvaluateAsync(Trip trip, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var conflicts = new List<PlanningConflictDto>();

        if (trip.DriverId is null)
        {
            conflicts.Add(new(PlanningConflictCode.MissingDriver, true, "Er is nog geen chauffeur toegewezen."));
        }

        if (trip.VehicleId is null)
        {
            conflicts.Add(new(PlanningConflictCode.MissingVehicle, true, "Er is nog geen voertuig toegewezen."));
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        if (orderIds.Count == 0)
        {
            conflicts.Add(new(PlanningConflictCode.NoOrders, true, "De rit bevat nog geen opdrachten."));
        }

        var orders = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.OrderNumber, o.AdrRequired, o.CraneRequired })
                .ToListAsync(cancellationToken);

        if (trip.DriverId is { } driverId)
        {
            await EvaluateDriverAsync(conflicts, trip, driverId, cancellationToken);
        }

        if (trip.VehicleId is { } vehicleId)
        {
            var vehicle = await _dbContext.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vehicleId && v.TenantId == tenantId, cancellationToken);
            if (vehicle is not null)
            {
                if (vehicle.OperationalStatus is VehicleOperationalStatus.InMaintenance or VehicleOperationalStatus.OutOfService)
                {
                    conflicts.Add(new(PlanningConflictCode.VehicleNotOperational, true,
                        $"Voertuig {vehicle.InternalNumber} is niet inzetbaar ({vehicle.OperationalStatus})."));
                }
                else if (vehicle.OperationalStatus == VehicleOperationalStatus.InUse)
                {
                    // Warning only: "in gebruik" may simply reflect this very trip's planning.
                    conflicts.Add(new(PlanningConflictCode.VehicleNotOperational, false,
                        $"Voertuig {vehicle.InternalNumber} staat gemarkeerd als in gebruik."));
                }

                if (!vehicle.IsActive)
                {
                    conflicts.Add(new(PlanningConflictCode.VehicleInactive, true,
                        $"Voertuig {vehicle.InternalNumber} is inactief."));
                }

                var craneOrders = orders.Where(o => o.CraneRequired).Select(o => o.OrderNumber).ToList();
                if (craneOrders.Count > 0 && !vehicle.HasCrane)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresCrane, true,
                        $"Opdracht(en) {string.Join(", ", craneOrders)} vereisen een kraan; {vehicle.InternalNumber} heeft er geen."));
                }

                var adrOrders = orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber).ToList();
                if (adrOrders.Count > 0 && !vehicle.AdrSuitable)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresAdr, true,
                        $"Opdracht(en) {string.Join(", ", adrOrders)} vereisen ADR; {vehicle.InternalNumber} is niet ADR-geschikt."));
                }

                var doubleBooked = await OtherTripAsync(trip, t => t.VehicleId == vehicleId, cancellationToken);
                if (doubleBooked is not null)
                {
                    conflicts.Add(new(PlanningConflictCode.VehicleDoubleBooked, true,
                        $"Voertuig {vehicle.InternalNumber} is op {trip.TripDate:dd-MM-yyyy} al ingepland op rit {doubleBooked}."));
                }
            }
        }

        if (trip.TrailerId is { } trailerId)
        {
            var trailer = await _dbContext.Trailers.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == trailerId && t.TenantId == tenantId, cancellationToken);
            if (trailer is not null)
            {
                if (trailer.OperationalStatus is TrailerOperationalStatus.InMaintenance or TrailerOperationalStatus.OutOfService)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerNotOperational, true,
                        $"Oplegger {trailer.InternalNumber} is niet inzetbaar ({trailer.OperationalStatus})."));
                }
                else if (trailer.OperationalStatus == TrailerOperationalStatus.InUse)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerNotOperational, false,
                        $"Oplegger {trailer.InternalNumber} staat gemarkeerd als in gebruik."));
                }

                if (!trailer.IsActive)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerInactive, true,
                        $"Oplegger {trailer.InternalNumber} is inactief."));
                }

                var adrOrders = orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber).ToList();
                if (adrOrders.Count > 0 && !trailer.AdrSuitable)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresAdr, true,
                        $"Opdracht(en) {string.Join(", ", adrOrders)} vereisen ADR; oplegger {trailer.InternalNumber} is niet ADR-geschikt."));
                }

                var doubleBooked = await OtherTripAsync(trip, t => t.TrailerId == trailerId, cancellationToken);
                if (doubleBooked is not null)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerDoubleBooked, true,
                        $"Oplegger {trailer.InternalNumber} is op {trip.TripDate:dd-MM-yyyy} al ingepland op rit {doubleBooked}."));
                }
            }
        }

        return conflicts;
    }

    private async Task EvaluateDriverAsync(
        List<PlanningConflictDto> conflicts, Trip trip, Guid driverId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var driver = await _dbContext.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driverId && d.TenantId == tenantId, cancellationToken);
        if (driver is null)
        {
            return;
        }

        var driverName = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == driver.EmployeeId && e.TenantId == tenantId)
            .Select(e => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? driver.DriverNumber;

        if (driver.IsBlocked)
        {
            conflicts.Add(new(PlanningConflictCode.DriverBlocked, true,
                $"Chauffeur {driverName} is geblokkeerd{(driver.BlockReason is { Length: > 0 } r ? $": {r}" : ".")}"));
        }

        if (!driver.IsActive)
        {
            conflicts.Add(new(PlanningConflictCode.DriverInactive, true, $"Chauffeur {driverName} is inactief."));
        }

        var absence = await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == driver.EmployeeId
                        && a.Status == AbsenceStatus.Approved
                        && a.StartDate <= trip.TripDate && a.EndDate >= trip.TripDate)
            .Select(a => (AbsenceType?)a.Type)
            .FirstOrDefaultAsync(cancellationToken);
        if (absence is { } absenceType)
        {
            conflicts.Add(new(PlanningConflictCode.DriverAbsent, true,
                $"Chauffeur {driverName} is afwezig op {trip.TripDate:dd-MM-yyyy} ({absenceType})."));
        }

        // Qualification state: expired/suspended/rejected blocks, expiring soon warns.
        var warningDays = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.QualificationExpiryWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultExpiryWarningDays;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var qualifications = await _dbContext.EmployeeQualifications.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.EmployeeId == driver.EmployeeId)
            .Join(_dbContext.QualificationTypes.AsNoTracking(),
                q => q.QualificationTypeId, t => t.Id,
                (q, t) => new { Qualification = q, t.Name })
            .ToListAsync(cancellationToken);

        foreach (var entry in qualifications)
        {
            var status = _statusCalculator.CalculateEffectiveStatus(entry.Qualification, today, warningDays);
            switch (status)
            {
                case QualificationStatus.Expired:
                case QualificationStatus.Suspended:
                case QualificationStatus.Rejected:
                    conflicts.Add(new(PlanningConflictCode.DriverNotReady, true,
                        $"{entry.Name} van {driverName} is niet geldig ({status})."));
                    break;
                case QualificationStatus.ExpiringSoon:
                    conflicts.Add(new(PlanningConflictCode.DriverNotReady, false,
                        $"{entry.Name} van {driverName} verloopt binnenkort."));
                    break;
            }
        }

        var doubleBooked = await OtherTripAsync(trip, t => t.DriverId == driverId, cancellationToken);
        if (doubleBooked is not null)
        {
            conflicts.Add(new(PlanningConflictCode.DriverDoubleBooked, true,
                $"Chauffeur {driverName} is op {trip.TripDate:dd-MM-yyyy} al ingepland op rit {doubleBooked}."));
        }
    }

    /// <summary>Trip number of another occupying trip on the same date matching the resource filter, or null.</summary>
    private async Task<string?> OtherTripAsync(
        Trip trip, System.Linq.Expressions.Expression<Func<Trip, bool>> resourceFilter, CancellationToken cancellationToken)
    {
        return await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId
                        && t.Id != trip.Id
                        && t.TripDate == trip.TripDate
                        && OccupyingStatuses.Contains(t.Status))
            .Where(resourceFilter)
            .Select(t => t.TripNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
