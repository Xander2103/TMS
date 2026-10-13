using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Services;

public interface IDockPlanningService
{
    Task<DockBoardDto?> GetBoardAsync(Guid warehouseId, DateOnly date, CancellationToken cancellationToken);
    Task<DockOperationResult> CreateAsync(SaveDockAppointmentRequest request, CancellationToken cancellationToken);
    Task<DockOperationResult> UpdateAsync(Guid id, SaveDockAppointmentRequest request, CancellationToken cancellationToken);
    Task<DockOperationResult> ChangeStatusAsync(Guid id, ChangeDockAppointmentStatusRequest request, CancellationToken cancellationToken);
    Task<DockOperationResult> DeleteAsync(Guid id, Guid? version, CancellationToken cancellationToken);
    Task<WarehouseDashboardDto?> GetDashboardAsync(Guid warehouseId, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// Dock appointment scheduling: backend-enforced conflicts (overlap, dock compatibility,
/// opening hours, duration), the controlled status machine, optimistic concurrency and a
/// reasoned override trail (warehouse.conflict_override) via the shared ConflictOverride
/// table. Scan progress derives from the EXISTING package lifecycle — never a second one.
/// </summary>
public class DockPlanningService : IDockPlanningService
{
    private const string EntityType = "DockAppointment";
    private const int MinimumDurationMinutes = 15;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public DockPlanningService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private IQueryable<DockAppointment> TenantScoped() =>
        _dbContext.DockAppointments.Where(a => a.TenantId == _tenantContext.TenantId);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    // -----------------------------------------------------------------------
    // Board + dashboard
    // -----------------------------------------------------------------------

    public async Task<DockBoardDto?> GetBoardAsync(Guid warehouseId, DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var warehouse = await _dbContext.Warehouses.AsNoTracking()
            .Include(w => w.Docks)
            .FirstOrDefaultAsync(w => w.Id == warehouseId && w.TenantId == tenantId, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var appointments = await TenantScoped().AsNoTracking()
            .Where(a => a.WarehouseId == warehouseId && a.PlannedStart < dayEnd && a.PlannedEnd > dayStart)
            .OrderBy(a => a.PlannedStart)
            .ToListAsync(cancellationToken);

        var dtos = await MapManyAsync(appointments, warehouse, cancellationToken);
        var queue = dtos
            .Where(a => a.DockId == null
                        && a.Status is DockAppointmentStatus.Arrived or DockAppointmentStatus.Waiting)
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.ArrivedAt)
            .ToList();

        return new DockBoardDto(
            warehouseId, date, warehouse.OpensAt, warehouse.ClosesAt,
            warehouse.Docks.Where(d => !d.IsDeleted).OrderBy(d => d.Code)
                .Select(d => new DockDto(d.Id, d.Code, d.Name, d.AllowsLoading, d.AllowsUnloading,
                    d.AllowsAdr, d.Refrigerated, d.MaxVehicleLengthM, d.MaxVehicleHeightM, d.IsActive, d.Notes))
                .ToList(),
            dtos, queue);
    }

    public async Task<WarehouseDashboardDto?> GetDashboardAsync(
        Guid warehouseId, DateOnly date, CancellationToken cancellationToken)
    {
        var board = await GetBoardAsync(warehouseId, date, cancellationToken);
        if (board is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var appointments = board.Appointments;

        // Delay = planned start passed without arrival, or handling past planned end.
        var delayed = appointments.Count(a =>
            (a.Status is DockAppointmentStatus.Planned or DockAppointmentStatus.Expected && a.PlannedStart < now)
            || (a.Status is DockAppointmentStatus.InProgress && a.PlannedEnd < now));

        var openMinutes = board.OpensAt is { } opens && board.ClosesAt is { } closes
            ? (int)(closes - opens).TotalMinutes
            : 24 * 60;
        var utilization = board.Docks
            .Where(d => d.IsActive)
            .Select(d =>
            {
                var booked = appointments
                    .Where(a => a.DockId == d.Id && a.Status != DockAppointmentStatus.Cancelled
                                && a.Status != DockAppointmentStatus.NoShow)
                    .Sum(a => (int)(a.PlannedEnd - a.PlannedStart).TotalMinutes);
                return new DockUtilizationDto(d.Id, d.Code, booked, openMinutes,
                    openMinutes > 0 ? Math.Round(Math.Min(100m, booked * 100m / openMinutes), 1) : 0m);
            })
            .ToList();

        return new WarehouseDashboardDto(
            warehouseId, date,
            appointments.Count(a => a.Status is DockAppointmentStatus.Planned or DockAppointmentStatus.Expected),
            appointments.Count(a => a.Status is DockAppointmentStatus.Arrived or DockAppointmentStatus.Waiting),
            appointments.Count(a => a.Status is DockAppointmentStatus.AssignedToDock or DockAppointmentStatus.InProgress),
            appointments.Count(a => a.Status == DockAppointmentStatus.Completed),
            delayed,
            appointments.Count(a => a.Status == DockAppointmentStatus.NoShow),
            utilization);
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    public async Task<DockOperationResult> CreateAsync(
        SaveDockAppointmentRequest request, CancellationToken cancellationToken)
    {
        var appointment = new DockAppointment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
        };
        return await SaveAsync(appointment, request, isNew: true, cancellationToken);
    }

    public async Task<DockOperationResult> UpdateAsync(
        Guid id, SaveDockAppointmentRequest request, CancellationToken cancellationToken)
    {
        var appointment = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return DockOperationResult.NotFound;
        }

        if (DockAppointmentStatusMachine.IsTerminal(appointment.Status))
        {
            return DockOperationResult.InvalidState("Een afgesloten afspraak kan niet meer worden gewijzigd.");
        }

        if (request.Version is { } expected && expected != appointment.Version)
        {
            return DockOperationResult.Stale(await MapAsync(appointment, cancellationToken));
        }

        return await SaveAsync(appointment, request, isNew: false, cancellationToken);
    }

    private async Task<DockOperationResult> SaveAsync(
        DockAppointment appointment, SaveDockAppointmentRequest request, bool isNew, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var warehouse = await _dbContext.Warehouses.AsNoTracking()
            .Include(w => w.Docks)
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.TenantId == tenantId, cancellationToken);
        if (warehouse is null)
        {
            return DockOperationResult.InvalidReference("Het magazijn bestaat niet.");
        }

        var plannedStart = AsUtc(request.PlannedStart);
        var plannedEnd = AsUtc(request.PlannedEnd);
        if (plannedEnd <= plannedStart)
        {
            return DockOperationResult.Invalid("Het einde moet na de start liggen.");
        }

        if (await ValidateReferencesAsync(request, cancellationToken) is { } referenceError)
        {
            return DockOperationResult.InvalidReference(referenceError);
        }

        // Structured conflict evaluation; blocking conflicts require the override permission
        // (guarded in the controller) plus a mandatory reason.
        var conflicts = await EvaluateConflictsAsync(
            appointment.Id, warehouse, request.DockId, request.OperationType,
            plannedStart, plannedEnd, request.TransportOrderId, cancellationToken);
        var blocking = conflicts.Where(c => c.Severity == ConflictSeverity.Blocking).ToList();
        if (blocking.Count > 0)
        {
            if (!request.Override)
            {
                return DockOperationResult.Blocked(conflicts);
            }

            if (string.IsNullOrWhiteSpace(request.OverrideReason))
            {
                return DockOperationResult.Invalid(
                    "Bij het overschrijven van blokkerende dockconflicten is een reden verplicht.");
            }

            _dbContext.Add(new ConflictOverride
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityType = EntityType,
                EntityId = appointment.Id,
                ConflictCodes = string.Join(",", blocking.Select(c => c.Code).Distinct()),
                Reason = request.OverrideReason.Trim(),
                OccurredAt = DateTime.UtcNow,
            });
        }

        var before = isNew ? null : new
        {
            appointment.DockId, appointment.PlannedStart, appointment.PlannedEnd, appointment.Status,
        };

        appointment.WarehouseId = request.WarehouseId;
        appointment.DockId = request.DockId;
        appointment.OperationType = request.OperationType;
        appointment.PlannedStart = plannedStart;
        appointment.PlannedEnd = plannedEnd;
        appointment.TripId = request.TripId;
        appointment.TransportOrderId = request.TransportOrderId;
        appointment.VehicleId = request.VehicleId;
        appointment.TrailerId = request.TrailerId;
        appointment.DriverId = request.DriverId;
        appointment.Priority = request.Priority;
        appointment.Reference = Trim(request.Reference);
        appointment.Remarks = Trim(request.Remarks);
        appointment.Version = Guid.NewGuid();

        if (isNew)
        {
            _dbContext.Add(appointment);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await TenantScoped().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointment.Id, cancellationToken);
            return current is null
                ? DockOperationResult.NotFound
                : DockOperationResult.Stale(await MapAsync(current, cancellationToken));
        }

        await _auditService.RecordAsync(EntityType, appointment.Id.ToString(),
            isNew ? "Created" : "Updated", before,
            new
            {
                appointment.WarehouseId, appointment.DockId, appointment.OperationType,
                appointment.PlannedStart, appointment.PlannedEnd,
                Overridden = request.Override ? request.OverrideReason : null,
            }, cancellationToken);

        return DockOperationResult.Success(await MapAsync(appointment, cancellationToken));
    }

    public async Task<DockOperationResult> ChangeStatusAsync(
        Guid id, ChangeDockAppointmentStatusRequest request, CancellationToken cancellationToken)
    {
        var appointment = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return DockOperationResult.NotFound;
        }

        if (request.Version is { } expected && expected != appointment.Version)
        {
            return DockOperationResult.Stale(await MapAsync(appointment, cancellationToken));
        }

        if (!DockAppointmentStatusMachine.IsAllowed(appointment.Status, request.Status))
        {
            var allowed = DockAppointmentStatusMachine.AllowedTargets(appointment.Status);
            return DockOperationResult.InvalidState(
                $"Een afspraak met status '{appointment.Status}' kan niet naar '{request.Status}'. "
                + $"Toegestaan: {(allowed.Count == 0 ? "geen (eindstatus)" : string.Join(", ", allowed))}.");
        }

        // Handling can only start ON a dock.
        if (request.Status == DockAppointmentStatus.InProgress && appointment.DockId is null)
        {
            return DockOperationResult.Invalid("Wijs eerst een dock toe voordat de behandeling start.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var before = new { appointment.Status };
        appointment.Status = request.Status;
        switch (request.Status)
        {
            case DockAppointmentStatus.Arrived:
                appointment.ArrivedAt ??= now;
                break;
            case DockAppointmentStatus.InProgress:
                appointment.StartedAt ??= now;
                break;
            case DockAppointmentStatus.Completed:
                appointment.CompletedAt ??= now;
                break;
        }

        appointment.Version = Guid.NewGuid();
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await TenantScoped().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            return current is null
                ? DockOperationResult.NotFound
                : DockOperationResult.Stale(await MapAsync(current, cancellationToken));
        }

        await _auditService.RecordAsync(EntityType, appointment.Id.ToString(), request.Status.ToString(),
            before, new { appointment.Status, appointment.DockId }, cancellationToken);

        return DockOperationResult.Success(await MapAsync(appointment, cancellationToken));
    }

    public async Task<DockOperationResult> DeleteAsync(Guid id, Guid? version, CancellationToken cancellationToken)
    {
        var appointment = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return DockOperationResult.NotFound;
        }

        if (version is { } expected && expected != appointment.Version)
        {
            return DockOperationResult.Stale(await MapAsync(appointment, cancellationToken));
        }

        if (appointment.Status is not (DockAppointmentStatus.Planned or DockAppointmentStatus.Expected
            or DockAppointmentStatus.Cancelled))
        {
            return DockOperationResult.InvalidState(
                "Alleen geplande of verwachte afspraken kunnen worden verwijderd; gebruik anders Annuleren.");
        }

        _dbContext.Remove(appointment); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, appointment.Id.ToString(), "Deleted",
            new { appointment.WarehouseId, appointment.PlannedStart }, null, cancellationToken);

        return DockOperationResult.Success(await MapAsync(appointment, cancellationToken));
    }

    // -----------------------------------------------------------------------
    // Conflicts
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<DockConflictDto>> EvaluateConflictsAsync(
        Guid appointmentId, Warehouse warehouse, Guid? dockId, DockOperationType operationType,
        DateTime plannedStart, DateTime plannedEnd, Guid? transportOrderId, CancellationToken cancellationToken)
    {
        var conflicts = new List<DockConflictDto>();

        if (!warehouse.IsActive)
        {
            conflicts.Add(new("WarehouseInactive", ConflictSeverity.Blocking,
                $"Magazijn {warehouse.Name} is inactief.", true));
        }

        if ((plannedEnd - plannedStart).TotalMinutes < MinimumDurationMinutes)
        {
            conflicts.Add(new("DurationTooShort", ConflictSeverity.Blocking,
                $"Een dockafspraak duurt minstens {MinimumDurationMinutes} minuten.", false));
        }

        // Opening hours (both endpoints inside the daily window).
        if (warehouse.OpensAt is { } opens && warehouse.ClosesAt is { } closes)
        {
            var startTime = TimeOnly.FromDateTime(plannedStart);
            var endTime = TimeOnly.FromDateTime(plannedEnd);
            if (startTime < opens || endTime > closes || endTime < startTime)
            {
                conflicts.Add(new("OutsideOpeningHours", ConflictSeverity.Blocking,
                    $"De afspraak valt buiten de openingsuren ({opens:HH\\:mm}–{closes:HH\\:mm}).", true));
            }
        }

        if (dockId is { } targetDockId)
        {
            var dock = warehouse.Docks.FirstOrDefault(d => d.Id == targetDockId && !d.IsDeleted);
            if (dock is null)
            {
                conflicts.Add(new("UnknownDock", ConflictSeverity.Blocking,
                    "Het gekozen dock hoort niet bij dit magazijn.", false));
            }
            else
            {
                if (!dock.IsActive)
                {
                    conflicts.Add(new("DockInactive", ConflictSeverity.Blocking,
                        $"Dock {dock.Code} is inactief.", true));
                }

                if (operationType == DockOperationType.Loading && !dock.AllowsLoading)
                {
                    conflicts.Add(new("DockTypeMismatch", ConflictSeverity.Blocking,
                        $"Dock {dock.Code} laat geen laadoperaties toe.", true));
                }

                if (operationType == DockOperationType.Unloading && !dock.AllowsUnloading)
                {
                    conflicts.Add(new("DockTypeMismatch", ConflictSeverity.Blocking,
                        $"Dock {dock.Code} laat geen losoperaties toe.", true));
                }

                if (transportOrderId is { } orderId)
                {
                    var adrRequired = await _dbContext.TransportOrders.AsNoTracking()
                        .Where(o => o.Id == orderId && o.TenantId == _tenantContext.TenantId)
                        .Select(o => (bool?)o.AdrRequired)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (adrRequired == true && !dock.AllowsAdr)
                    {
                        conflicts.Add(new("AdrNotAllowed", ConflictSeverity.Blocking,
                            $"De opdracht vereist ADR; dock {dock.Code} is daar niet voor uitgerust.", true));
                    }
                }

                // Overlap on the same dock with any occupying appointment.
                var overlapping = await TenantScoped().AsNoTracking()
                    .Where(a => a.Id != appointmentId && a.DockId == targetDockId
                                && a.PlannedStart < plannedEnd && a.PlannedEnd > plannedStart)
                    .ToListAsync(cancellationToken);
                var occupying = overlapping.FirstOrDefault(a => DockAppointmentStatusMachine.Occupies(a.Status));
                if (occupying is not null)
                {
                    conflicts.Add(new("DockOverlap", ConflictSeverity.Blocking,
                        $"Dock {dock.Code} is al bezet van {occupying.PlannedStart:HH\\:mm} tot {occupying.PlannedEnd:HH\\:mm}.",
                        true));
                }
            }
        }
        else
        {
            conflicts.Add(new("NoDockAssigned", ConflictSeverity.Information,
                "Nog geen dock toegewezen; de afspraak komt in de wachtrij.", false));
        }

        return conflicts;
    }

    private async Task<string?> ValidateReferencesAsync(
        SaveDockAppointmentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (request.TripId is { } tripId && !await _dbContext.Trips.AnyAsync(
                t => t.Id == tripId && t.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde rit bestaat niet.";
        }

        if (request.TransportOrderId is { } orderId && !await _dbContext.TransportOrders.AnyAsync(
                o => o.Id == orderId && o.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde opdracht bestaat niet.";
        }

        if (request.VehicleId is { } vehicleId && !await _dbContext.Vehicles.AnyAsync(
                v => v.Id == vehicleId && v.TenantId == tenantId, cancellationToken))
        {
            return "Het gekoppelde voertuig bestaat niet.";
        }

        if (request.TrailerId is { } trailerId && !await _dbContext.Trailers.AnyAsync(
                t => t.Id == trailerId && t.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde oplegger bestaat niet.";
        }

        if (request.DriverId is { } driverId && !await _dbContext.Drivers.AnyAsync(
                d => d.Id == driverId && d.TenantId == tenantId, cancellationToken))
        {
            return "De gekoppelde chauffeur bestaat niet.";
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Mapping
    // -----------------------------------------------------------------------

    private async Task<DockAppointmentDto> MapAsync(DockAppointment appointment, CancellationToken cancellationToken)
    {
        var warehouse = await _dbContext.Warehouses.AsNoTracking()
            .Include(w => w.Docks)
            .FirstOrDefaultAsync(w => w.Id == appointment.WarehouseId && w.TenantId == _tenantContext.TenantId,
                cancellationToken);
        return (await MapManyAsync([appointment], warehouse, cancellationToken))[0];
    }

    private async Task<IReadOnlyList<DockAppointmentDto>> MapManyAsync(
        IReadOnlyList<DockAppointment> appointments, Warehouse? warehouse, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dockCodes = (warehouse?.Docks ?? []).ToDictionary(d => d.Id, d => d.Code);

        var tripIds = appointments.Where(a => a.TripId != null).Select(a => a.TripId!.Value).Distinct().ToList();
        var tripNumbers = tripIds.Count == 0
            ? []
            : await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId && tripIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.TripNumber, cancellationToken);

        var orderIds = appointments.Where(a => a.TransportOrderId != null)
            .Select(a => a.TransportOrderId!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? []
            : await (from o in _dbContext.TransportOrders.AsNoTracking()
                         .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                     join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                         on o.CustomerId equals c.Id
                     select new { o.Id, o.OrderNumber, CustomerName = c.Name })
                .ToDictionaryAsync(o => o.Id, cancellationToken);

        var vehicleIds = appointments.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value).Distinct().ToList();
        var vehicles = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.InternalNumber, cancellationToken);
        var trailerIds = appointments.Where(a => a.TrailerId != null).Select(a => a.TrailerId!.Value).Distinct().ToList();
        var trailers = trailerIds.Count == 0
            ? []
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.InternalNumber, cancellationToken);
        var driverIds = appointments.Where(a => a.DriverId != null).Select(a => a.DriverId!.Value).Distinct().ToList();
        var drivers = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        // Scan progress from the existing package lifecycle (no second lifecycle).
        var packages = orderIds.Count == 0
            ? []
            : await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && orderIds.Contains(p.TransportOrderId))
                .Select(p => new { p.TransportOrderId, p.CurrentLifecycleStatus, p.CurrentExceptionStatus })
                .ToListAsync(cancellationToken);
        var packagesByOrder = packages.ToLookup(p => p.TransportOrderId);

        return appointments.Select(a =>
        {
            var order = a.TransportOrderId is { } oid ? orders.GetValueOrDefault(oid) : null;
            var orderPackages = a.TransportOrderId is { } pid ? packagesByOrder[pid].ToList() : [];
            var handled = orderPackages.Count(p => a.OperationType == DockOperationType.Loading
                ? p.CurrentLifecycleStatus is not (PackageLifecycleStatus.Created
                    or PackageLifecycleStatus.Labelled or PackageLifecycleStatus.AwaitingLoading)
                : p.CurrentLifecycleStatus is PackageLifecycleStatus.Delivered
                    or PackageLifecycleStatus.PartiallyDelivered or PackageLifecycleStatus.ReturnedToDepot);

            return new DockAppointmentDto(
                a.Id, a.WarehouseId, a.DockId,
                a.DockId is { } did ? dockCodes.GetValueOrDefault(did) : null,
                a.OperationType, a.Status, a.PlannedStart, a.PlannedEnd,
                a.ArrivedAt, a.StartedAt, a.CompletedAt, a.Priority,
                a.TripId, a.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null,
                a.TransportOrderId, order?.OrderNumber, order?.CustomerName,
                a.VehicleId, a.VehicleId is { } vid ? vehicles.GetValueOrDefault(vid) : null,
                a.TrailerId, a.TrailerId is { } trid ? trailers.GetValueOrDefault(trid) : null,
                a.DriverId, a.DriverId is { } drid ? drivers.GetValueOrDefault(drid) : null,
                a.Reference, a.Remarks,
                orderPackages.Count, handled,
                orderPackages.Any(p => p.CurrentExceptionStatus == PackageExceptionState.Open),
                DockAppointmentStatusMachine.AllowedTargets(a.Status),
                a.Version);
        }).ToList();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
