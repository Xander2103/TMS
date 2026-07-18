using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class InspectionService : IInspectionService
{
    private const string EntityType = "Inspection";

    /// <summary>Default warning window when an inspection has no per-inspection override.</summary>
    public const int DefaultWarningDays = 30;

    /// <summary>Crane inspections recur every 3 months unless configured otherwise.</summary>
    public const int CraneDefaultIntervalMonths = 3;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public InspectionService(
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

    private IQueryable<Inspection> TenantScoped() =>
        _dbContext.Set<Inspection>().Where(i => i.TenantId == _tenantContext.TenantId);

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<InspectionDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Vehicles.AnyAsync(
                v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(i => i.VehicleId == vehicleId, cancellationToken);
    }

    public async Task<IReadOnlyList<InspectionDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Trailers.AnyAsync(
                t => t.Id == trailerId && t.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(i => i.TrailerId == trailerId, cancellationToken);
    }

    public async Task<IReadOnlyList<DueInspectionDto>> ListDueAsync(int withinDays, CancellationToken cancellationToken)
    {
        var today = Today;
        var horizon = today.AddDays(Math.Clamp(withinDays, 0, 365));

        var rows = await (
                from i in TenantScoped().AsNoTracking()
                where i.CompletedDate == null && i.DueDate <= horizon
                join v in _dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == _tenantContext.TenantId)
                    on i.VehicleId equals v.Id into vehicles
                from v in vehicles.DefaultIfEmpty()
                join t in _dbContext.Trailers.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId)
                    on i.TrailerId equals t.Id into trailers
                from t in trailers.DefaultIfEmpty()
                orderby i.DueDate
                select new
                {
                    i.Id, i.VehicleId, i.TrailerId, i.InspectionType, i.CustomTypeName, i.DueDate, i.WarningDays,
                    OwnerNumber = v != null ? v.InternalNumber : t != null ? t.InternalNumber : string.Empty,
                    OwnerPlate = v != null ? v.LicensePlate : t != null ? t.LicensePlate : string.Empty,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DueInspectionDto(
                r.Id, r.VehicleId, r.TrailerId, r.OwnerNumber, r.OwnerPlate,
                r.InspectionType, r.CustomTypeName, r.DueDate,
                ComputeUrgency(r.DueDate, completedDate: null, r.WarningDays, today)))
            .ToList();
    }

    public Task<InspectionOperationResult> CreateForVehicleAsync(
        Guid vehicleId, CreateInspectionRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId, trailerId: null, request, cancellationToken);

    public Task<InspectionOperationResult> CreateForTrailerAsync(
        Guid trailerId, CreateInspectionRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId: null, trailerId, request, cancellationToken);

    public async Task<InspectionOperationResult> UpdateAsync(
        Guid id, UpdateInspectionRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.InspectionType, request.CustomTypeName) is { } error)
        {
            return InspectionOperationResult.Invalid(error);
        }

        var inspection = await TenantScoped().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (inspection is null)
        {
            return InspectionOperationResult.NotFound;
        }

        var before = new { inspection.InspectionType, inspection.DueDate };

        inspection.InspectionType = request.InspectionType;
        inspection.CustomTypeName = request.InspectionType == InspectionType.Other ? Trim(request.CustomTypeName) : null;
        inspection.DueDate = request.DueDate;
        inspection.IntervalMonths = NormalizeInterval(request.IntervalMonths);
        inspection.WarningDays = request.WarningDays is { } days ? Math.Clamp(days, 0, 365) : null;
        inspection.Notes = Trim(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, inspection.Id.ToString(), "Updated", before,
            new { inspection.InspectionType, inspection.DueDate }, cancellationToken);

        return InspectionOperationResult.Success(Map(inspection));
    }

    public async Task<InspectionOperationResult> CompleteAsync(
        Guid id, CompleteInspectionRequest request, CancellationToken cancellationToken)
    {
        var inspection = await TenantScoped().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (inspection is null)
        {
            return InspectionOperationResult.NotFound;
        }

        if (inspection.CompletedDate is not null)
        {
            return InspectionOperationResult.AlreadyCompleted;
        }

        var before = new { inspection.CompletedDate, inspection.Result };

        inspection.CompletedDate = request.CompletedDate;
        inspection.Result = request.Result;
        inspection.Notes = Trim(request.Notes) ?? inspection.Notes;

        // A failed inspection needs a re-inspection, not the next periodic one — only plan the
        // recurring follow-up on a pass.
        Inspection? followUp = null;
        if (inspection.IntervalMonths is { } months && request.Result != InspectionResult.Failed)
        {
            followUp = new Inspection
            {
                Id = Guid.NewGuid(),
                TenantId = inspection.TenantId,
                VehicleId = inspection.VehicleId,
                TrailerId = inspection.TrailerId,
                InspectionType = inspection.InspectionType,
                CustomTypeName = inspection.CustomTypeName,
                DueDate = request.CompletedDate.AddMonths(months),
                IntervalMonths = months,
                WarningDays = inspection.WarningDays,
            };
            _dbContext.Add(followUp);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, inspection.Id.ToString(), "Completed", before,
            new { inspection.CompletedDate, inspection.Result, FollowUpId = followUp?.Id }, cancellationToken);

        return InspectionOperationResult.Success(Map(inspection), followUp is null ? null : Map(followUp));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var inspection = await TenantScoped().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (inspection is null)
        {
            return false;
        }

        _dbContext.Remove(inspection); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, inspection.Id.ToString(), "Deleted",
            new { inspection.InspectionType, inspection.DueDate }, null, cancellationToken);

        return true;
    }

    private async Task<InspectionOperationResult> CreateAsync(
        Guid? vehicleId, Guid? trailerId, CreateInspectionRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.InspectionType, request.CustomTypeName) is { } error)
        {
            return InspectionOperationResult.Invalid(error);
        }

        var ownerExists = vehicleId is { } v
            ? await _dbContext.Vehicles.AnyAsync(x => x.Id == v && x.TenantId == _tenantContext.TenantId, cancellationToken)
            : await _dbContext.Trailers.AnyAsync(x => x.Id == trailerId && x.TenantId == _tenantContext.TenantId, cancellationToken);

        if (!ownerExists)
        {
            return InspectionOperationResult.OwnerNotFound;
        }

        var inspection = new Inspection
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            TrailerId = trailerId,
            InspectionType = request.InspectionType,
            CustomTypeName = request.InspectionType == InspectionType.Other ? Trim(request.CustomTypeName) : null,
            DueDate = request.DueDate,
            // Crane inspections default to the statutory 3-month cadence when no interval is given.
            IntervalMonths = NormalizeInterval(request.IntervalMonths)
                ?? (request.InspectionType == InspectionType.CraneInspection ? CraneDefaultIntervalMonths : null),
            WarningDays = request.WarningDays is { } days ? Math.Clamp(days, 0, 365) : null,
            Notes = Trim(request.Notes),
        };

        _dbContext.Add(inspection);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, inspection.Id.ToString(), "Created", null,
            new { inspection.InspectionType, inspection.VehicleId, inspection.TrailerId, inspection.DueDate }, cancellationToken);

        return InspectionOperationResult.Success(Map(inspection));
    }

    private async Task<IReadOnlyList<InspectionDto>> ListOwnedAsync(
        System.Linq.Expressions.Expression<Func<Inspection, bool>> ownerFilter, CancellationToken cancellationToken)
    {
        var inspections = await TenantScoped()
            .AsNoTracking()
            .Where(ownerFilter)
            .OrderByDescending(i => i.CompletedDate == null)
            .ThenBy(i => i.DueDate)
            .ToListAsync(cancellationToken);

        return inspections.Select(Map).ToList();
    }

    private static string? Validate(InspectionType type, string? customTypeName)
    {
        if (type == InspectionType.Other && string.IsNullOrWhiteSpace(customTypeName))
        {
            return "Een naam voor het aangepaste keuringstype is verplicht.";
        }

        return null;
    }

    private static int? NormalizeInterval(int? value) =>
        value is { } v ? Math.Clamp(v, 1, 120) : null;

    private InspectionDto Map(Inspection i) => new(
        i.Id, i.VehicleId, i.TrailerId, i.InspectionType, i.CustomTypeName,
        i.DueDate, i.CompletedDate, i.Result, i.IntervalMonths, i.WarningDays,
        ComputeUrgency(i.DueDate, i.CompletedDate, i.WarningDays, Today),
        HasAttachment: i.AttachmentPath is not null,
        i.Notes);

    public static InspectionUrgency ComputeUrgency(DateOnly dueDate, DateOnly? completedDate, int? warningDays, DateOnly today)
    {
        if (completedDate is not null)
        {
            return InspectionUrgency.Completed;
        }

        if (dueDate < today)
        {
            return InspectionUrgency.Overdue;
        }

        var window = warningDays ?? DefaultWarningDays;
        return dueDate <= today.AddDays(window) ? InspectionUrgency.DueSoon : InspectionUrgency.Ok;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
