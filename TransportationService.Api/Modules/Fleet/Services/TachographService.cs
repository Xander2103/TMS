using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public enum TachographStatus { Valid, ExpiringSoon, Overdue }

public record TachographCalibrationDto(
    Guid Id, Guid VehicleId, string? TachographType, string? Manufacturer, string? Model, string? SerialNumber,
    DateOnly CalibrationDate, DateOnly NextCalibrationDue, string? Workshop, string? CertificateNumber, string? SealReference,
    int? OdometerKm, int? TyreCircumferenceMm, string? Notes, bool HasAttachment, string? FileName, TachographStatus Status);

public record SaveTachographCalibrationRequest(
    string? TachographType, string? Manufacturer, string? Model, string? SerialNumber,
    DateOnly CalibrationDate, DateOnly NextCalibrationDue, string? Workshop, string? CertificateNumber, string? SealReference,
    int? OdometerKm, int? TyreCircumferenceMm, string? Notes);

public interface ITachographService
{
    Task<IReadOnlyList<TachographCalibrationDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);
    Task<TachographCalibrationDto?> CreateAsync(Guid vehicleId, SaveTachographCalibrationRequest request, CancellationToken cancellationToken);
    Task<TachographCalibrationDto?> UpdateAsync(Guid vehicleId, Guid id, SaveTachographCalibrationRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid vehicleId, Guid id, CancellationToken cancellationToken);
    Task<TachographCalibrationDto?> AttachFileAsync(Guid vehicleId, Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid vehicleId, Guid id, CancellationToken cancellationToken);

    /// <summary>Vehicle ids whose latest calibration is overdue (for fleet readiness / conflicts).</summary>
    Task<IReadOnlySet<Guid>> OverdueVehicleIdsAsync(CancellationToken cancellationToken);
}

public class TachographService : ITachographService
{
    private const string EntityType = "TachographCalibration";
    private const string StorageCategory = "tachograph";
    private const int WarningDays = 60;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IFileStorageService _fileStorage;

    public TachographService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, TimeProvider timeProvider, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _fileStorage = fileStorage;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<TachographCalibrationDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await VehicleExistsAsync(vehicleId, cancellationToken))
        {
            return null;
        }

        var rows = await _dbContext.TachographCalibrations.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.VehicleId == vehicleId)
            .OrderByDescending(t => t.CalibrationDate)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<TachographCalibrationDto?> CreateAsync(Guid vehicleId, SaveTachographCalibrationRequest request, CancellationToken cancellationToken)
    {
        if (!await VehicleExistsAsync(vehicleId, cancellationToken))
        {
            return null;
        }

        Validate(request);
        var entity = new TachographCalibration { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, VehicleId = vehicleId };
        Apply(entity, request);
        _dbContext.TachographCalibrations.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "Created", null,
            new { entity.VehicleId, entity.CalibrationDate, entity.NextCalibrationDue }, cancellationToken);
        return Map(entity);
    }

    public async Task<TachographCalibrationDto?> UpdateAsync(Guid vehicleId, Guid id, SaveTachographCalibrationRequest request, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(vehicleId, id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        Validate(request);
        Apply(entity, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "Updated", null,
            new { entity.CalibrationDate, entity.NextCalibrationDue }, cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid vehicleId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(vehicleId, id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "Deleted", new { entity.CalibrationDate }, null, cancellationToken);
        return true;
    }

    public async Task<TachographCalibrationDto?> AttachFileAsync(Guid vehicleId, Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(vehicleId, id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var previous = entity.StorageKey;
        entity.StorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        entity.FileName = fileName;
        entity.ContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (previous is not null)
        {
            await _fileStorage.DeleteAsync(previous, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "DocumentUploaded", null, new { FileName = fileName }, cancellationToken);
        return Map(entity);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid vehicleId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(vehicleId, id, cancellationToken);
        if (entity?.StorageKey is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(entity.StorageKey, cancellationToken);
        return (stream, entity.FileName ?? "tachograaf", entity.ContentType ?? "application/octet-stream");
    }

    public async Task<IReadOnlySet<Guid>> OverdueVehicleIdsAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        // Latest calibration per vehicle; overdue when its next-due date is in the past.
        var latest = await _dbContext.TachographCalibrations.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId)
            .GroupBy(t => t.VehicleId)
            .Select(g => new { VehicleId = g.Key, NextDue = g.Max(x => x.NextCalibrationDue) })
            .ToListAsync(cancellationToken);
        return latest.Where(x => x.NextDue < today).Select(x => x.VehicleId).ToHashSet();
    }

    private void Validate(SaveTachographCalibrationRequest request)
    {
        if (request.NextCalibrationDue < request.CalibrationDate)
        {
            throw new DomainValidationException("nextCalibrationDue", "De volgende ijkdatum kan niet vóór de ijkdatum liggen.");
        }
    }

    private static void Apply(TachographCalibration e, SaveTachographCalibrationRequest r)
    {
        e.TachographType = Trim(r.TachographType);
        e.Manufacturer = Trim(r.Manufacturer);
        e.Model = Trim(r.Model);
        e.SerialNumber = Trim(r.SerialNumber);
        e.CalibrationDate = r.CalibrationDate;
        e.NextCalibrationDue = r.NextCalibrationDue;
        e.Workshop = Trim(r.Workshop);
        e.CertificateNumber = Trim(r.CertificateNumber);
        e.SealReference = Trim(r.SealReference);
        e.OdometerKm = r.OdometerKm is < 0 ? null : r.OdometerKm;
        e.TyreCircumferenceMm = r.TyreCircumferenceMm is < 0 ? null : r.TyreCircumferenceMm;
        e.Notes = Trim(r.Notes);
    }

    private Task<TachographCalibration?> FindAsync(Guid vehicleId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.TachographCalibrations.FirstOrDefaultAsync(
            t => t.TenantId == _tenantContext.TenantId && t.VehicleId == vehicleId && t.Id == id, cancellationToken)!;

    private Task<bool> VehicleExistsAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        _dbContext.Vehicles.AnyAsync(v => v.TenantId == _tenantContext.TenantId && v.Id == vehicleId, cancellationToken);

    private TachographCalibrationDto Map(TachographCalibration t) => new(
        t.Id, t.VehicleId, t.TachographType, t.Manufacturer, t.Model, t.SerialNumber,
        t.CalibrationDate, t.NextCalibrationDue, t.Workshop, t.CertificateNumber, t.SealReference,
        t.OdometerKm, t.TyreCircumferenceMm, t.Notes, t.StorageKey is not null, t.FileName,
        ComputeStatus(t.NextCalibrationDue, Today));

    public static TachographStatus ComputeStatus(DateOnly nextDue, DateOnly today)
    {
        if (nextDue < today)
        {
            return TachographStatus.Overdue;
        }

        return nextDue <= today.AddDays(WarningDays) ? TachographStatus.ExpiringSoon : TachographStatus.Valid;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
