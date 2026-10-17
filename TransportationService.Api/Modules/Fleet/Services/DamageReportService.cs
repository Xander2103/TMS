using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class DamageReportService : IDamageReportService
{
    private const string EntityType = "DamageReport";
    private const string StorageCategory = "damage-reports";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<DamageReportService>? _logger;
    private readonly IFileStorageService? _fileStorage;

    public DamageReportService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        INotificationEventService? notificationEvents = null, ILogger<DamageReportService>? logger = null,
        IFileStorageService? fileStorage = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _notificationEvents = notificationEvents;
        _logger = logger;
        _fileStorage = fileStorage;
    }

    private IQueryable<DamageReport> TenantScoped() =>
        _dbContext.Set<DamageReport>().Where(d => d.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<DamageReportDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Vehicles.AnyAsync(
                v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(d => d.VehicleId == vehicleId, cancellationToken);
    }

    public async Task<IReadOnlyList<DamageReportDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Trailers.AnyAsync(
                t => t.Id == trailerId && t.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(d => d.TrailerId == trailerId, cancellationToken);
    }

    public async Task<IReadOnlyList<RecentDamageDto>> ListRecentAsync(int take, CancellationToken cancellationToken)
    {
        return await (
                from d in TenantScoped().AsNoTracking()
                join v in _dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == _tenantContext.TenantId)
                    on d.VehicleId equals v.Id into vehicles
                from v in vehicles.DefaultIfEmpty()
                join t in _dbContext.Trailers.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId)
                    on d.TrailerId equals t.Id into trailers
                from t in trailers.DefaultIfEmpty()
                orderby d.IncidentDate descending, d.CreatedAt descending
                select new RecentDamageDto(
                    d.Id, d.VehicleId, d.TrailerId,
                    v != null ? v.InternalNumber : t != null ? t.InternalNumber : string.Empty,
                    v != null ? v.LicensePlate : t != null ? t.LicensePlate : string.Empty,
                    d.IncidentDate, d.Severity, d.Status, d.Description))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public Task<DamageOperationResult> CreateForVehicleAsync(
        Guid vehicleId, CreateDamageReportRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId, trailerId: null, request, cancellationToken);

    public Task<DamageOperationResult> CreateForTrailerAsync(
        Guid trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId: null, trailerId, request, cancellationToken);

    public async Task<DamageOperationResult> UpdateAsync(
        Guid id, UpdateDamageReportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return DamageOperationResult.Invalid("Een omschrijving is verplicht.");
        }

        var report = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (report is null)
        {
            return DamageOperationResult.NotFound;
        }

        if (!await DriverInTenantAsync(request.DriverId, cancellationToken))
        {
            return DamageOperationResult.InvalidReference;
        }

        if (await TripErrorAsync(request.TripId, report.VehicleId, report.TrailerId, cancellationToken) is { } tripError)
        {
            return DamageOperationResult.Invalid(tripError);
        }

        if (request.RepairCost is < 0 || request.DowntimeDays is < 0)
        {
            return DamageOperationResult.Invalid("Herstelkost en stilstand kunnen niet negatief zijn.");
        }

        var before = new { report.Status, report.Severity, report.RepairCost, report.DowntimeDays, report.TripId };

        report.DriverId = request.DriverId;
        report.TripId = request.TripId;
        report.IncidentDate = request.IncidentDate;
        report.Location = Trim(request.Location);
        report.Description = request.Description.Trim();
        report.Severity = request.Severity;
        report.Status = request.Status;
        report.InsuranceReference = Trim(request.InsuranceReference);
        report.RepairCost = request.RepairCost is < 0 ? 0 : request.RepairCost;
        report.DowntimeDays = request.DowntimeDays is { } days ? Math.Max(0, days) : null;
        report.Notes = Trim(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, report.Id.ToString(), "Updated", before,
            new { report.Status, report.Severity, report.RepairCost, report.DowntimeDays, report.TripId }, cancellationToken);

        return DamageOperationResult.Success(await MapAsync(report, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (report is null)
        {
            return false;
        }

        _dbContext.Remove(report); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, report.Id.ToString(), "Deleted",
            new { report.IncidentDate, report.Description }, null, cancellationToken);

        return true;
    }

    private async Task<DamageOperationResult> CreateAsync(
        Guid? vehicleId, Guid? trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return DamageOperationResult.Invalid("Een omschrijving is verplicht.");
        }

        var ownerExists = vehicleId is { } v
            ? await _dbContext.Vehicles.AnyAsync(x => x.Id == v && x.TenantId == _tenantContext.TenantId, cancellationToken)
            : await _dbContext.Trailers.AnyAsync(x => x.Id == trailerId && x.TenantId == _tenantContext.TenantId, cancellationToken);

        if (!ownerExists)
        {
            return DamageOperationResult.OwnerNotFound;
        }

        if (!await DriverInTenantAsync(request.DriverId, cancellationToken))
        {
            return DamageOperationResult.InvalidReference;
        }

        if (await TripErrorAsync(request.TripId, vehicleId, trailerId, cancellationToken) is { } tripError)
        {
            return DamageOperationResult.Invalid(tripError);
        }

        if (request.RepairCost is < 0 || request.DowntimeDays is < 0)
        {
            return DamageOperationResult.Invalid("Herstelkost en stilstand kunnen niet negatief zijn.");
        }

        var report = new DamageReport
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            TrailerId = trailerId,
            DriverId = request.DriverId,
            IncidentDate = request.IncidentDate,
            Location = Trim(request.Location),
            Description = request.Description.Trim(),
            Severity = request.Severity,
            Status = DamageStatus.Reported,
            InsuranceReference = Trim(request.InsuranceReference),
            Notes = Trim(request.Notes),
            TripId = request.TripId,
            RepairCost = request.RepairCost,
            DowntimeDays = request.DowntimeDays,
        };

        _dbContext.Add(report);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, report.Id.ToString(), "Created", null,
            new { report.IncidentDate, report.Severity, report.VehicleId, report.TrailerId, report.DriverId, report.TripId, report.RepairCost }, cancellationToken);

        if (_notificationEvents is not null)
        {
            try
            {
                var vehicleLabel = report.VehicleId is { } vId
                    ? await _dbContext.Vehicles.AsNoTracking().Where(v => v.Id == vId).Select(v => v.LicensePlate).FirstOrDefaultAsync(cancellationToken)
                    : report.TrailerId is { } tId
                        ? await _dbContext.Trailers.AsNoTracking().Where(t => t.Id == tId).Select(t => t.LicensePlate).FirstOrDefaultAsync(cancellationToken)
                        : null;
                await _notificationEvents.PublishAsync(MessageKinds.FleetDamageCreated, new NotificationEventContext(
                    EntityType, report.Id.ToString(),
                    new Dictionary<string, string> { ["vehicle"] = vehicleLabel ?? "onbekend", ["description"] = report.Description })
                {
                    LinkPath = report.VehicleId is { } linkVehicleId ? $"/vehicles/{linkVehicleId}?tab=schade" : "/trailers",
                    InAppMessage = $"Nieuw schadegeval ({vehicleLabel ?? "onbekend"}): {report.Description}",
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.",
                    MessageKinds.FleetDamageCreated);
            }
        }

        return DamageOperationResult.Success(await MapAsync(report, cancellationToken));
    }

    private async Task<IReadOnlyList<DamageReportDto>> ListOwnedAsync(
        System.Linq.Expressions.Expression<Func<DamageReport, bool>> ownerFilter, CancellationToken cancellationToken)
    {
        var rows = await (
                from d in TenantScoped().AsNoTracking().Where(ownerFilter)
                join drv in _dbContext.Drivers.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId)
                    on d.DriverId equals drv.Id into drivers
                from drv in drivers.DefaultIfEmpty()
                join e in _dbContext.Employees.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId)
                    on drv.EmployeeId equals e.Id into employees
                from e in employees.DefaultIfEmpty()
                orderby d.IncidentDate descending, d.CreatedAt descending
                join trip in _dbContext.Trips.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId)
                    on d.TripId equals trip.Id into trips
                from trip in trips.DefaultIfEmpty()
                select new
                {
                    Report = d,
                    DriverName = e != null ? e.FirstName + " " + e.LastName : null,
                    TripNumber = trip != null ? trip.TripNumber : null,
                })
            .ToListAsync(cancellationToken);

        return rows.Select(r => Map(r.Report, r.DriverName, r.TripNumber)).ToList();
    }

    private async Task<bool> DriverInTenantAsync(Guid? driverId, CancellationToken cancellationToken) =>
        driverId is not { } id
        || await _dbContext.Drivers.AnyAsync(
            d => d.Id == id && d.TenantId == _tenantContext.TenantId, cancellationToken);

    private async Task<DamageReportDto> MapAsync(DamageReport report, CancellationToken cancellationToken)
    {
        string? driverName = report.DriverId is { } driverId
            ? await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.Id == driverId && d.TenantId == _tenantContext.TenantId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var tripNumber = report.TripId is { } tripId
            ? await _dbContext.Trips.AsNoTracking()
                .Where(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId)
                .Select(t => t.TripNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return Map(report, driverName, tripNumber);
    }

    private static DamageReportDto Map(DamageReport d, string? driverName, string? tripNumber) => new(
        d.Id, d.VehicleId, d.TrailerId, d.DriverId, driverName,
        d.IncidentDate, d.Location, d.Description, d.Severity, d.Status,
        d.InsuranceReference, d.RepairCost, d.DowntimeDays,
        HasAttachment: d.AttachmentPath is not null,
        d.Notes,
        d.TripId,
        tripNumber);

    /// <summary>FL-M7: a linked trip must exist in this tenant and carry the same vehicle/trailer.</summary>
    private async Task<string?> TripErrorAsync(Guid? tripId, Guid? vehicleId, Guid? trailerId, CancellationToken cancellationToken)
    {
        if (tripId is not { } id)
        {
            return null;
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.Id == id && t.TenantId == _tenantContext.TenantId)
            .Select(t => new { t.VehicleId, t.TrailerId })
            .FirstOrDefaultAsync(cancellationToken);
        var matches = trip is not null
                      && ((vehicleId is not null && trip.VehicleId == vehicleId)
                          || (trailerId is not null && trip.TrailerId == trailerId));
        return matches ? null : (vehicleId is not null
            ? "De gekozen rit hoort niet bij dit voertuig."
            : "De gekozen rit hoort niet bij deze oplegger.");
    }

    public async Task<DamageOperationResult> AttachFileAsync(
        Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var report = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (report is null)
        {
            return DamageOperationResult.NotFound;
        }

        if (_fileStorage is null)
        {
            return DamageOperationResult.Invalid("Bestandsopslag is niet beschikbaar.");
        }

        var previousKey = report.AttachmentPath;
        report.AttachmentPath = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        report.AttachmentFileName = fileName;
        report.AttachmentContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (previousKey is not null)
        {
            await _fileStorage.DeleteAsync(previousKey, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, report.Id.ToString(), "DocumentUploaded", null,
            new { FileName = fileName }, cancellationToken);

        return DamageOperationResult.Success(await MapAsync(report, cancellationToken));
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (report?.AttachmentPath is null || _fileStorage is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(report.AttachmentPath, cancellationToken);
        return (stream, report.AttachmentFileName ?? "schade", report.AttachmentContentType ?? "application/octet-stream");
    }

    public async Task<DamageOperationResult> RemoveFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (report is null)
        {
            return DamageOperationResult.NotFound;
        }

        if (report.AttachmentPath is { } key)
        {
            report.AttachmentPath = null;
            report.AttachmentFileName = null;
            report.AttachmentContentType = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (_fileStorage is not null)
            {
                await _fileStorage.DeleteAsync(key, cancellationToken);
            }

            await _auditService.RecordAsync(EntityType, report.Id.ToString(), "DocumentRemoved", null, null, cancellationToken);
        }

        return DamageOperationResult.Success(await MapAsync(report, cancellationToken));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
