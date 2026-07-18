using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class MaintenanceService : IMaintenanceService
{
    private const string EntityType = "MaintenanceRecord";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public MaintenanceService(
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

    private IQueryable<MaintenanceRecord> TenantScoped() =>
        _dbContext.Set<MaintenanceRecord>().Where(m => m.TenantId == _tenantContext.TenantId);

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<MaintenanceRecordDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        var vehicle = await _dbContext.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        var records = await TenantScoped().AsNoTracking()
            .Where(m => m.VehicleId == vehicleId)
            .OrderByDescending(m => m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress)
            .ThenBy(m => m.ScheduledDate)
            .ThenByDescending(m => m.CompletedDate)
            .ToListAsync(cancellationToken);

        return records.Select(m => Map(m, vehicle.OdometerKm)).ToList();
    }

    public async Task<IReadOnlyList<MaintenanceRecordDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Trailers.AnyAsync(
                t => t.Id == trailerId && t.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        var records = await TenantScoped().AsNoTracking()
            .Where(m => m.TrailerId == trailerId)
            .OrderByDescending(m => m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress)
            .ThenBy(m => m.ScheduledDate)
            .ThenByDescending(m => m.CompletedDate)
            .ToListAsync(cancellationToken);

        return records.Select(m => Map(m, currentOdometerKm: null)).ToList();
    }

    public async Task<IReadOnlyList<DueMaintenanceDto>> ListDueAsync(int withinDays, CancellationToken cancellationToken)
    {
        var today = Today;
        var horizon = today.AddDays(Math.Clamp(withinDays, 0, 365));

        var rows = await (
                from m in TenantScoped().AsNoTracking()
                where m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress
                join v in _dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == _tenantContext.TenantId)
                    on m.VehicleId equals v.Id into vehicles
                from v in vehicles.DefaultIfEmpty()
                join t in _dbContext.Trailers.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId)
                    on m.TrailerId equals t.Id into trailers
                from t in trailers.DefaultIfEmpty()
                where (m.ScheduledDate != null && m.ScheduledDate <= horizon)
                      || (m.OdometerTriggerKm != null && v != null && v.OdometerKm >= m.OdometerTriggerKm)
                orderby m.ScheduledDate
                select new
                {
                    m.Id, m.VehicleId, m.TrailerId, m.MaintenanceType, m.CustomTypeName, m.Description,
                    m.Status, m.ScheduledDate, m.OdometerTriggerKm,
                    OwnerNumber = v != null ? v.InternalNumber : t != null ? t.InternalNumber : string.Empty,
                    OwnerPlate = v != null ? v.LicensePlate : t != null ? t.LicensePlate : string.Empty,
                    CurrentOdometer = v != null ? (int?)v.OdometerKm : null,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DueMaintenanceDto(
                r.Id, r.VehicleId, r.TrailerId, r.OwnerNumber, r.OwnerPlate,
                r.MaintenanceType, r.CustomTypeName, r.Description, r.Status,
                r.ScheduledDate, r.OdometerTriggerKm,
                IsOverdue(r.ScheduledDate, r.OdometerTriggerKm, r.CurrentOdometer, today)))
            .OrderByDescending(r => r.IsOverdue)
            .ThenBy(r => r.ScheduledDate ?? DateOnly.MaxValue)
            .ToList();
    }

    public Task<MaintenanceOperationResult> CreateForVehicleAsync(
        Guid vehicleId, CreateMaintenanceRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId, trailerId: null, request, cancellationToken);

    public Task<MaintenanceOperationResult> CreateForTrailerAsync(
        Guid trailerId, CreateMaintenanceRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId: null, trailerId, request, cancellationToken);

    public async Task<MaintenanceOperationResult> UpdateAsync(
        Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.MaintenanceType, request.CustomTypeName, request.Description, trailerOnly: false, request.OdometerTriggerKm) is { } error)
        {
            return MaintenanceOperationResult.Invalid(error);
        }

        var record = await TenantScoped().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (record is null)
        {
            return MaintenanceOperationResult.NotFound;
        }

        if (record.TrailerId is not null && request.OdometerTriggerKm is not null)
        {
            return MaintenanceOperationResult.Invalid("Een kilometertrigger is alleen mogelijk voor voertuigen.");
        }

        var before = new { record.MaintenanceType, record.Status, record.ScheduledDate };

        record.MaintenanceType = request.MaintenanceType;
        record.CustomTypeName = request.MaintenanceType == MaintenanceType.Other ? Trim(request.CustomTypeName) : null;
        record.Status = request.Status;
        record.Description = request.Description.Trim();
        record.ScheduledDate = request.ScheduledDate;
        record.OdometerTriggerKm = request.OdometerTriggerKm;
        record.Provider = Trim(request.Provider);
        record.IntervalMonths = NormalizeInterval(request.IntervalMonths, 120);
        record.IntervalKm = NormalizeInterval(request.IntervalKm, 1_000_000);
        record.Notes = Trim(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, record.Id.ToString(), "Updated", before,
            new { record.MaintenanceType, record.Status, record.ScheduledDate }, cancellationToken);

        return MaintenanceOperationResult.Success(await MapWithOwnerOdometerAsync(record, cancellationToken));
    }

    public async Task<MaintenanceOperationResult> CompleteAsync(
        Guid id, CompleteMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var record = await TenantScoped().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (record is null)
        {
            return MaintenanceOperationResult.NotFound;
        }

        if (record.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            return MaintenanceOperationResult.AlreadyCompleted;
        }

        var before = new { record.Status, record.CompletedDate };

        record.Status = MaintenanceStatus.Completed;
        record.CompletedDate = request.CompletedDate;
        record.CompletedOdometerKm = request.CompletedOdometerKm;
        record.WorkPerformed = Trim(request.WorkPerformed);
        record.Provider = Trim(request.Provider) ?? record.Provider;
        record.Cost = request.Cost;
        record.Notes = Trim(request.Notes) ?? record.Notes;

        // Recurrence: derive the follow-up trigger(s) from the interval(s).
        MaintenanceRecord? followUp = null;
        if (record.IntervalMonths is not null || record.IntervalKm is not null)
        {
            record.NextServiceDate = record.IntervalMonths is { } months
                ? request.CompletedDate.AddMonths(months)
                : null;
            record.NextServiceOdometerKm = record.IntervalKm is { } km && request.CompletedOdometerKm is { } odo
                ? odo + km
                : null;

            followUp = new MaintenanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = record.TenantId,
                VehicleId = record.VehicleId,
                TrailerId = record.TrailerId,
                MaintenanceType = record.MaintenanceType,
                CustomTypeName = record.CustomTypeName,
                Status = MaintenanceStatus.Planned,
                Description = record.Description,
                ScheduledDate = record.NextServiceDate,
                OdometerTriggerKm = record.NextServiceOdometerKm,
                Provider = record.Provider,
                IntervalMonths = record.IntervalMonths,
                IntervalKm = record.IntervalKm,
            };
            _dbContext.Add(followUp);
        }

        // Completing a vehicle job with a higher odometer reading also advances the vehicle.
        if (record.VehicleId is { } vehicleId && request.CompletedOdometerKm is { } completedKm)
        {
            var vehicle = await _dbContext.Vehicles
                .FirstOrDefaultAsync(v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken);
            if (vehicle is not null && completedKm > vehicle.OdometerKm)
            {
                vehicle.OdometerKm = completedKm;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, record.Id.ToString(), "Completed", before,
            new { record.Status, record.CompletedDate, record.Cost, FollowUpId = followUp?.Id }, cancellationToken);

        return MaintenanceOperationResult.Success(
            await MapWithOwnerOdometerAsync(record, cancellationToken),
            followUp is null ? null : await MapWithOwnerOdometerAsync(followUp, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await TenantScoped().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (record is null)
        {
            return false;
        }

        _dbContext.Remove(record); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, record.Id.ToString(), "Deleted",
            new { record.MaintenanceType, record.Description }, null, cancellationToken);

        return true;
    }

    private async Task<MaintenanceOperationResult> CreateAsync(
        Guid? vehicleId, Guid? trailerId, CreateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.MaintenanceType, request.CustomTypeName, request.Description,
                trailerOnly: trailerId is not null, request.OdometerTriggerKm) is { } error)
        {
            return MaintenanceOperationResult.Invalid(error);
        }

        var ownerExists = vehicleId is { } v
            ? await _dbContext.Vehicles.AnyAsync(x => x.Id == v && x.TenantId == _tenantContext.TenantId, cancellationToken)
            : await _dbContext.Trailers.AnyAsync(x => x.Id == trailerId && x.TenantId == _tenantContext.TenantId, cancellationToken);

        if (!ownerExists)
        {
            return MaintenanceOperationResult.OwnerNotFound;
        }

        var record = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            TrailerId = trailerId,
            MaintenanceType = request.MaintenanceType,
            CustomTypeName = request.MaintenanceType == MaintenanceType.Other ? Trim(request.CustomTypeName) : null,
            Status = MaintenanceStatus.Planned,
            Description = request.Description.Trim(),
            ScheduledDate = request.ScheduledDate,
            OdometerTriggerKm = request.OdometerTriggerKm,
            Provider = Trim(request.Provider),
            IntervalMonths = NormalizeInterval(request.IntervalMonths, 120),
            IntervalKm = NormalizeInterval(request.IntervalKm, 1_000_000),
            Notes = Trim(request.Notes),
        };

        _dbContext.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, record.Id.ToString(), "Created", null,
            new { record.MaintenanceType, record.VehicleId, record.TrailerId, record.ScheduledDate }, cancellationToken);

        return MaintenanceOperationResult.Success(await MapWithOwnerOdometerAsync(record, cancellationToken));
    }

    private static string? Validate(
        MaintenanceType type, string? customTypeName, string description, bool trailerOnly, int? odometerTriggerKm)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "Een omschrijving is verplicht.";
        }

        if (type == MaintenanceType.Other && string.IsNullOrWhiteSpace(customTypeName))
        {
            return "Een naam voor het aangepaste onderhoudstype is verplicht.";
        }

        if (trailerOnly && odometerTriggerKm is not null)
        {
            return "Een kilometertrigger is alleen mogelijk voor voertuigen.";
        }

        if (odometerTriggerKm is < 0)
        {
            return "De kilometertrigger kan niet negatief zijn.";
        }

        return null;
    }

    private static int? NormalizeInterval(int? value, int max) =>
        value is { } v ? Math.Clamp(v, 1, max) : null;

    private async Task<MaintenanceRecordDto> MapWithOwnerOdometerAsync(MaintenanceRecord record, CancellationToken cancellationToken)
    {
        int? currentOdometer = record.VehicleId is { } vehicleId
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId)
                .Select(v => (int?)v.OdometerKm)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return Map(record, currentOdometer);
    }

    private MaintenanceRecordDto Map(MaintenanceRecord m, int? currentOdometerKm) => new(
        m.Id, m.VehicleId, m.TrailerId, m.MaintenanceType, m.CustomTypeName, m.Status,
        IsOverdue(m, currentOdometerKm, Today),
        m.Description, m.ScheduledDate, m.OdometerTriggerKm,
        m.CompletedDate, m.CompletedOdometerKm, m.WorkPerformed, m.Provider, m.Cost,
        m.NextServiceDate, m.NextServiceOdometerKm, m.IntervalMonths, m.IntervalKm,
        HasAttachment: m.AttachmentPath is not null,
        m.Notes);

    private static bool IsOverdue(MaintenanceRecord m, int? currentOdometerKm, DateOnly today) =>
        m.Status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress
        && IsOverdue(m.ScheduledDate, m.OdometerTriggerKm, currentOdometerKm, today);

    public static bool IsOverdue(DateOnly? scheduledDate, int? odometerTriggerKm, int? currentOdometerKm, DateOnly today)
    {
        if (scheduledDate is { } scheduled && scheduled < today)
        {
            return true;
        }

        return odometerTriggerKm is { } trigger && currentOdometerKm is { } current && current >= trigger;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
