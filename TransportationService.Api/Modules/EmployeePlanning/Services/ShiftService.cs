using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

public class ShiftService : IShiftService
{
    private const string EntityType = "Shift";

    private static readonly IReadOnlyDictionary<ShiftStatus, ShiftStatus[]> Transitions =
        new Dictionary<ShiftStatus, ShiftStatus[]>
        {
            [ShiftStatus.Draft] = [ShiftStatus.Planned],
            [ShiftStatus.Planned] = [ShiftStatus.Confirmed, ShiftStatus.Draft],
            [ShiftStatus.Confirmed] = [ShiftStatus.Planned],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly TimeProvider _timeProvider;

    public ShiftService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        INotificationService notificationService,
        ICalendarSyncService calendarSyncService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _calendarSyncService = calendarSyncService;
        _timeProvider = timeProvider;
    }

    private IQueryable<Shift> Scoped() => _dbContext.Shifts.Where(s => s.TenantId == _tenantContext.TenantId);

    private static int PlannedMinutes(TimeOnly start, TimeOnly end, int breakMinutes) =>
        (int)(end - start).TotalMinutes - breakMinutes;

    private static string? ShiftValidationError(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        if (end <= start)
        {
            return "Het einde van een shift moet na het begin liggen.";
        }

        if (breakMinutes < 0 || breakMinutes >= (int)(end - start).TotalMinutes)
        {
            return "De pauze moet korter zijn dan de shift zelf.";
        }

        return null;
    }

    public async Task<ShiftDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var shift = await Scoped().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return shift is null ? null : await MapAsync(shift, cancellationToken);
    }

    public async Task<ShiftOperationResult> CreateAsync(CreateShiftRequest request, CancellationToken cancellationToken)
    {
        if (ShiftValidationError(request.StartTime, request.EndTime, request.BreakMinutes) is { } error)
        {
            return ShiftOperationResult.Invalid(error);
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null)
        {
            return ShiftOperationResult.NotFound;
        }

        if (await HasOverlapAsync(request.EmployeeId, request.Date, request.StartTime, request.EndTime, excludeShiftId: null, cancellationToken))
        {
            return ShiftOperationResult.OverlapError("Deze medewerker heeft al een overlappende shift op die dag.");
        }

        var shift = new Shift
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = request.EmployeeId,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            Type = request.Type,
            Status = ShiftStatus.Draft,
            WorkLocation = Trim(request.WorkLocation),
            RoleLabel = Trim(request.RoleLabel),
            Notes = Trim(request.Notes),
        };
        _dbContext.Add(shift);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, shift.Id.ToString(), "Created", null,
            new { shift.EmployeeId, shift.Date, shift.StartTime, shift.EndTime, shift.Type }, cancellationToken);

        await NotifyEmployeeAsync(request.EmployeeId, "shift_assigned", "Nieuwe shift ingepland",
            $"{request.Date:dd-MM-yyyy} {request.StartTime:HH\\:mm}–{request.EndTime:HH\\:mm}"
            + (shift.WorkLocation is { } location ? $" · {location}" : string.Empty),
            cancellationToken);

        return ShiftOperationResult.Success(await MapAsync(shift, cancellationToken));
    }

    public async Task<ShiftOperationResult> UpdateAsync(Guid id, UpdateShiftRequest request, CancellationToken cancellationToken)
    {
        if (ShiftValidationError(request.StartTime, request.EndTime, request.BreakMinutes) is { } error)
        {
            return ShiftOperationResult.Invalid(error);
        }

        var shift = await Scoped().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return ShiftOperationResult.NotFound;
        }

        if (await HasOverlapAsync(shift.EmployeeId, request.Date, request.StartTime, request.EndTime, id, cancellationToken))
        {
            return ShiftOperationResult.OverlapError("Deze medewerker heeft al een overlappende shift op die dag.");
        }

        var before = new { shift.Date, shift.StartTime, shift.EndTime, shift.Type, shift.WorkLocation };
        shift.Date = request.Date;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.BreakMinutes = request.BreakMinutes;
        shift.Type = request.Type;
        shift.WorkLocation = Trim(request.WorkLocation);
        shift.RoleLabel = Trim(request.RoleLabel);
        shift.Notes = Trim(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, shift.Id.ToString(), "Updated", before,
            new { shift.Date, shift.StartTime, shift.EndTime, shift.Type, shift.WorkLocation }, cancellationToken);

        await NotifyEmployeeAsync(shift.EmployeeId, "planning_changed", "Shift aangepast",
            $"{shift.Date:dd-MM-yyyy} {shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}", cancellationToken);

        return ShiftOperationResult.Success(await MapAsync(shift, cancellationToken));
    }

    public async Task<ShiftOperationResult> ChangeStatusAsync(Guid id, ShiftStatus status, CancellationToken cancellationToken)
    {
        var shift = await Scoped().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return ShiftOperationResult.NotFound;
        }

        if (!Transitions[shift.Status].Contains(status))
        {
            return ShiftOperationResult.InvalidState(
                $"Een shift met status '{shift.Status}' kan niet naar '{status}'.");
        }

        var before = new { shift.Status };
        shift.Status = status;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, shift.Id.ToString(), "StatusChanged", before,
            new { shift.Status }, cancellationToken);

        if (status == ShiftStatus.Confirmed)
        {
            await NotifyEmployeeAsync(shift.EmployeeId, "shift_assigned", "Shift bevestigd",
                $"{shift.Date:dd-MM-yyyy} {shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}", cancellationToken);

            await _calendarSyncService.QueueAsync(new CalendarSyncEvent(
                "shift_confirmed", shift.Id, shift.EmployeeId, shift.Date, shift.Date,
                $"Shift {shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}"
                + (shift.WorkLocation is { } location ? $" · {location}" : string.Empty)), cancellationToken);
        }
        else if (before.Status == ShiftStatus.Confirmed)
        {
            await _calendarSyncService.CancelAsync("shift_confirmed", shift.Id, cancellationToken);
        }

        return ShiftOperationResult.Success(await MapAsync(shift, cancellationToken));
    }

    public async Task<ShiftOperationResult> CopyWeekAsync(CopyWeekRequest request, CancellationToken cancellationToken)
    {
        var offsetDays = request.TargetWeekStart.DayNumber - request.SourceWeekStart.DayNumber;
        if (offsetDays == 0)
        {
            return ShiftOperationResult.Invalid("De doelweek moet verschillen van de bronweek.");
        }

        var sourceEnd = request.SourceWeekStart.AddDays(6);
        var query = Scoped().AsNoTracking()
            .Where(s => s.Date >= request.SourceWeekStart && s.Date <= sourceEnd);
        if (request.EmployeeId is { } employeeId)
        {
            query = query.Where(s => s.EmployeeId == employeeId);
        }

        if (request.DepartmentId is { } departmentId)
        {
            var departmentEmployees = _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == _tenantContext.TenantId && e.DepartmentId == departmentId)
                .Select(e => e.Id);
            query = query.Where(s => departmentEmployees.Contains(s.EmployeeId));
        }

        var sourceShifts = await query.ToListAsync(cancellationToken);

        var copied = 0;
        var skipped = 0;
        foreach (var source in sourceShifts)
        {
            var targetDate = source.Date.AddDays(offsetDays);
            if (await HasOverlapAsync(source.EmployeeId, targetDate, source.StartTime, source.EndTime, null, cancellationToken))
            {
                skipped += 1;
                continue;
            }

            _dbContext.Add(new Shift
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = source.EmployeeId,
                Date = targetDate,
                StartTime = source.StartTime,
                EndTime = source.EndTime,
                BreakMinutes = source.BreakMinutes,
                Type = source.Type,
                Status = ShiftStatus.Draft,
                WorkLocation = source.WorkLocation,
                RoleLabel = source.RoleLabel,
                Notes = source.Notes,
            });
            copied += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, request.TargetWeekStart.ToString("yyyy-MM-dd"), "WeekCopied",
            new { request.SourceWeekStart }, new { request.TargetWeekStart, copied, skipped }, cancellationToken);

        return ShiftOperationResult.Copied(copied, skipped);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var shift = await Scoped().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return false;
        }

        _dbContext.Remove(shift); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, shift.Id.ToString(), "Deleted",
            new { shift.EmployeeId, shift.Date }, null, cancellationToken);

        await NotifyEmployeeAsync(shift.EmployeeId, "planning_changed", "Shift geannuleerd",
            $"{shift.Date:dd-MM-yyyy} {shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm} is geschrapt.", cancellationToken);

        if (shift.Status == ShiftStatus.Confirmed)
        {
            await _calendarSyncService.CancelAsync("shift_confirmed", shift.Id, cancellationToken);
        }

        return true;
    }

    public async Task<ScheduleGridDto> GetScheduleAsync(
        DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var employeesQuery = _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive);
        if (departmentId is { } d)
        {
            employeesQuery = employeesQuery.Where(e => e.DepartmentId == d);
        }

        if (employeeId is { } emp)
        {
            employeesQuery = employeesQuery.Where(e => e.Id == emp);
        }

        var employees = await employeesQuery
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.DepartmentId })
            .ToListAsync(cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToList();

        var departments = await _dbContext.Departments.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var shifts = await Scoped().AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to && employeeIds.Contains(s.EmployeeId))
            .ToListAsync(cancellationToken);

        var absences = await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == tenantId && employeeIds.Contains(a.EmployeeId)
                        && a.StartDate <= to && a.EndDate >= from
                        && (a.Status == AbsenceStatus.Requested || a.Status == AbsenceStatus.Approved
                            || a.Status == AbsenceStatus.Rejected))
            .ToListAsync(cancellationToken);

        var tripEntries = await _dbContext.TripPlanningEntries.AsNoTracking()
            .Where(t => t.TenantId == tenantId && employeeIds.Contains(t.EmployeeId)
                        && t.Date >= from && t.Date <= to)
            .ToListAsync(cancellationToken);

        var rows = employees.Select(employee =>
        {
            var employeeShifts = shifts.Where(s => s.EmployeeId == employee.Id).ToList();
            var employeeAbsences = absences.Where(a => a.EmployeeId == employee.Id).ToList();
            var employeeTrips = tripEntries.Where(t => t.EmployeeId == employee.Id).ToList();

            var days = new List<ScheduleDayDto>();
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                days.Add(new ScheduleDayDto(date, BuildEntries(date, employeeShifts, employeeAbsences, employeeTrips)));
            }

            var plannedMinutes = employeeShifts.Sum(s => PlannedMinutes(s.StartTime, s.EndTime, s.BreakMinutes));

            return new ScheduleRowDto(
                employee.Id,
                $"{employee.FirstName} {employee.LastName}",
                employee.EmployeeNumber,
                employee.DepartmentId is { } dep ? departments.GetValueOrDefault(dep) : null,
                plannedMinutes,
                days);
        }).ToList();

        return new ScheduleGridDto(from, to, rows);
    }

    public async Task<IReadOnlyList<ScheduleDayDto>> GetEmployeeScheduleAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var grid = await GetScheduleAsync(from, to, null, employeeId, cancellationToken);
        return grid.Rows.Count == 0 ? [] : grid.Rows[0].Days;
    }

    private static IReadOnlyList<ScheduleEntryDto> BuildEntries(
        DateOnly date, IReadOnlyList<Shift> shifts, IReadOnlyList<Absence> absences,
        IReadOnlyList<TripPlanningEntry> tripEntries)
    {
        var entries = new List<ScheduleEntryDto>();

        foreach (var shift in shifts.Where(s => s.Date == date).OrderBy(s => s.StartTime))
        {
            var state = shift.Type switch
            {
                ShiftType.Training => ScheduleEntryState.Training,
                ShiftType.Standby => ScheduleEntryState.Standby,
                _ => shift.Status switch
                {
                    ShiftStatus.Planned => ScheduleEntryState.Planned,
                    ShiftStatus.Confirmed => ScheduleEntryState.Confirmed,
                    _ => ScheduleEntryState.Draft,
                },
            };
            entries.Add(new ScheduleEntryDto(
                state, shift.Id, null, null, "Shift",
                shift.WorkLocation ?? shift.RoleLabel ?? "Shift",
                shift.StartTime, shift.EndTime, shift.Type, shift.WorkLocation,
                null, ShiftStatusLabel(shift.Status)));
        }

        foreach (var trip in tripEntries.Where(t => t.Date == date).OrderBy(t => t.PlannedStart ?? DateTime.MaxValue))
        {
            var start = trip.ActualStart ?? trip.PlannedStart;
            var end = trip.ActualEnd ?? trip.PlannedEnd;
            entries.Add(new ScheduleEntryDto(
                trip.Status == Planning.Entities.TripStatus.Cancelled
                    ? ScheduleEntryState.TripCancelled
                    : ScheduleEntryState.Trip,
                null, null, trip.TripId, "Trip",
                trip.TripNumber,
                start is { } s ? TimeOnly.FromDateTime(s) : null,
                end is { } e ? TimeOnly.FromDateTime(e) : null,
                null, trip.RouteSummary,
                trip.VehicleSummary, TripStatusLabel(trip.Status)));
        }

        foreach (var absence in absences.Where(a => a.StartDate <= date && a.EndDate >= date))
        {
            var state = absence.Status switch
            {
                AbsenceStatus.Requested => ScheduleEntryState.LeaveRequested,
                AbsenceStatus.Rejected => ScheduleEntryState.LeaveRejected,
                _ => absence.Type switch
                {
                    AbsenceType.Sick => ScheduleEntryState.Sick,
                    AbsenceType.Training => ScheduleEntryState.Training,
                    AbsenceType.Other => ScheduleEntryState.Unavailable,
                    _ => ScheduleEntryState.LeaveApproved,
                },
            };
            entries.Add(new ScheduleEntryDto(state, null, absence.Id, null, "Absence",
                AbsenceLabel(absence.Type), null, null, null, null, null, AbsenceStatusLabel(absence.Status)));
        }

        return entries;
    }

    private static string ShiftStatusLabel(ShiftStatus status) => status switch
    {
        ShiftStatus.Planned => "Gepland",
        ShiftStatus.Confirmed => "Bevestigd",
        _ => "Concept",
    };

    private static string TripStatusLabel(Planning.Entities.TripStatus status) => status switch
    {
        Planning.Entities.TripStatus.Planned => "Gepland",
        Planning.Entities.TripStatus.InProgress => "Bezig",
        Planning.Entities.TripStatus.Completed => "Afgerond",
        Planning.Entities.TripStatus.Cancelled => "Geannuleerd",
        _ => "Concept",
    };

    private static string AbsenceStatusLabel(AbsenceStatus status) => status switch
    {
        AbsenceStatus.Requested => "Aangevraagd",
        AbsenceStatus.Rejected => "Afgewezen",
        AbsenceStatus.UnderReview => "In behandeling",
        _ => "Goedgekeurd",
    };

    private static string AbsenceLabel(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "Verlof",
        AbsenceType.Sick => "Ziek",
        AbsenceType.Training => "Opleiding",
        AbsenceType.PersonalLeave => "Persoonlijk verlof",
        AbsenceType.Unpaid => "Onbetaald verlof",
        _ => "Onbeschikbaar",
    };

    private async Task<bool> HasOverlapAsync(
        Guid employeeId, DateOnly date, TimeOnly start, TimeOnly end, Guid? excludeShiftId, CancellationToken cancellationToken)
    {
        var query = Scoped().AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.Date == date
                        && s.StartTime < end && s.EndTime > start);
        if (excludeShiftId is { } exclude)
        {
            query = query.Where(s => s.Id != exclude);
        }

        return await query.AnyAsync(cancellationToken);
    }

    private async Task NotifyEmployeeAsync(
        Guid employeeId, string type, string title, string message, CancellationToken cancellationToken)
    {
        var userId = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.EmployeeId == employeeId && u.IsActive)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        await _notificationService.NotifyAsync(userId, type, title, message, "/portal/planning", cancellationToken);
    }

    private async Task<ShiftDto> MapAsync(Shift shift, CancellationToken cancellationToken)
    {
        var employeeName = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == shift.EmployeeId && e.TenantId == _tenantContext.TenantId)
            .Select(e => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new ShiftDto(
            shift.Id, shift.EmployeeId, employeeName,
            shift.Date, shift.StartTime, shift.EndTime, shift.BreakMinutes,
            PlannedMinutes(shift.StartTime, shift.EndTime, shift.BreakMinutes),
            shift.Type, shift.Status, shift.WorkLocation, shift.RoleLabel, shift.Notes);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
