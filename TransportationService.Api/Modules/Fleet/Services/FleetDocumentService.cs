using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class FleetDocumentService : IFleetDocumentService
{
    private const string EntityType = "FleetDocument";
    private const string StorageCategory = "fleet-documents";

    /// <summary>Default warning window when a document has no per-document override.</summary>
    public const int DefaultWarningDays = 60;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IFileStorageService _fileStorage;

    public FleetDocumentService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _fileStorage = fileStorage;
    }

    private IQueryable<FleetDocument> TenantScoped() =>
        _dbContext.Set<FleetDocument>().Where(d => d.TenantId == _tenantContext.TenantId);

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<FleetDocumentDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Vehicles.AnyAsync(
                v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(d => d.VehicleId == vehicleId, cancellationToken);
    }

    public async Task<IReadOnlyList<FleetDocumentDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Trailers.AnyAsync(
                t => t.Id == trailerId && t.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        return await ListOwnedAsync(d => d.TrailerId == trailerId, cancellationToken);
    }

    public async Task<IReadOnlyList<ExpiringFleetDocumentDto>> ListExpiringAsync(int withinDays, CancellationToken cancellationToken)
    {
        var today = Today;
        var horizon = today.AddDays(Math.Clamp(withinDays, 0, 365));

        // Pull candidates by horizon; per-document warning windows refine the status client-side
        // of the query (the row set is small: only documents near expiry).
        var rows = await (
                from d in TenantScoped().AsNoTracking()
                where d.ExpiryDate != null && d.ExpiryDate <= horizon
                join v in _dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == _tenantContext.TenantId)
                    on d.VehicleId equals v.Id into vehicles
                from v in vehicles.DefaultIfEmpty()
                join t in _dbContext.Trailers.AsNoTracking().Where(t => t.TenantId == _tenantContext.TenantId)
                    on d.TrailerId equals t.Id into trailers
                from t in trailers.DefaultIfEmpty()
                orderby d.ExpiryDate
                select new
                {
                    d.Id, d.VehicleId, d.TrailerId, d.DocumentType, d.CustomTypeName, d.ExpiryDate,
                    d.WarningDays,
                    OwnerNumber = v != null ? v.InternalNumber : t != null ? t.InternalNumber : string.Empty,
                    OwnerPlate = v != null ? v.LicensePlate : t != null ? t.LicensePlate : string.Empty,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ExpiringFleetDocumentDto(
                r.Id, r.VehicleId, r.TrailerId, r.OwnerNumber, r.OwnerPlate,
                r.DocumentType, r.CustomTypeName, r.ExpiryDate!.Value,
                ComputeStatus(r.ExpiryDate, r.WarningDays, today)))
            .ToList();
    }

    public Task<FleetDocumentOperationResult> CreateForVehicleAsync(
        Guid vehicleId, CreateFleetDocumentRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId, trailerId: null, request, cancellationToken);

    public Task<FleetDocumentOperationResult> CreateForTrailerAsync(
        Guid trailerId, CreateFleetDocumentRequest request, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId: null, trailerId, request, cancellationToken);

    public async Task<FleetDocumentOperationResult> UpdateAsync(
        Guid id, UpdateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.DocumentType, request.CustomTypeName, request.IssueDate, request.ExpiryDate) is { } error)
        {
            return FleetDocumentOperationResult.Invalid(error);
        }

        var document = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return FleetDocumentOperationResult.NotFound;
        }

        var before = new { document.DocumentType, document.DocumentNumber, document.ExpiryDate };

        document.DocumentType = request.DocumentType;
        document.CustomTypeName = request.DocumentType == FleetDocumentType.Other ? Trim(request.CustomTypeName) : null;
        document.DocumentNumber = Trim(request.DocumentNumber);
        document.IssueDate = request.IssueDate;
        document.ExpiryDate = request.ExpiryDate;
        document.WarningDays = NormalizeWarningDays(request.WarningDays);
        document.IssuingAuthority = Trim(request.IssuingAuthority);
        document.Notes = Trim(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Updated", before,
            new { document.DocumentType, document.DocumentNumber, document.ExpiryDate }, cancellationToken);

        return FleetDocumentOperationResult.Success(Map(document));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        _dbContext.Remove(document); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Deleted",
            new { document.DocumentType, document.DocumentNumber }, null, cancellationToken);

        return true;
    }

    private async Task<FleetDocumentOperationResult> CreateAsync(
        Guid? vehicleId, Guid? trailerId, CreateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.DocumentType, request.CustomTypeName, request.IssueDate, request.ExpiryDate) is { } error)
        {
            return FleetDocumentOperationResult.Invalid(error);
        }

        var ownerExists = vehicleId is { } v
            ? await _dbContext.Vehicles.AnyAsync(x => x.Id == v && x.TenantId == _tenantContext.TenantId, cancellationToken)
            : await _dbContext.Trailers.AnyAsync(x => x.Id == trailerId && x.TenantId == _tenantContext.TenantId, cancellationToken);

        if (!ownerExists)
        {
            return FleetDocumentOperationResult.OwnerNotFound;
        }

        var document = new FleetDocument
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            TrailerId = trailerId,
            DocumentType = request.DocumentType,
            CustomTypeName = request.DocumentType == FleetDocumentType.Other ? Trim(request.CustomTypeName) : null,
            DocumentNumber = Trim(request.DocumentNumber),
            IssueDate = request.IssueDate,
            ExpiryDate = request.ExpiryDate,
            WarningDays = NormalizeWarningDays(request.WarningDays),
            IssuingAuthority = Trim(request.IssuingAuthority),
            Notes = Trim(request.Notes),
        };

        _dbContext.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Created", null,
            new { document.DocumentType, document.VehicleId, document.TrailerId, document.ExpiryDate }, cancellationToken);

        return FleetDocumentOperationResult.Success(Map(document));
    }

    public async Task<FleetDocumentOperationResult> AttachFileAsync(
        Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var document = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return FleetDocumentOperationResult.NotFound;
        }

        var previousKey = document.DocumentPath;
        document.DocumentPath = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        document.FileName = fileName;
        document.ContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (previousKey is not null)
        {
            await _fileStorage.DeleteAsync(previousKey, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "DocumentUploaded", null,
            new { FileName = fileName }, cancellationToken);

        return FleetDocumentOperationResult.Success(Map(document));
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document?.DocumentPath is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.DocumentPath, cancellationToken);
        return (stream, document.FileName ?? "document", document.ContentType ?? "application/octet-stream");
    }

    public async Task<FleetDocumentOperationResult> RemoveFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return FleetDocumentOperationResult.NotFound;
        }

        if (document.DocumentPath is { } key)
        {
            document.DocumentPath = null;
            document.FileName = null;
            document.ContentType = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _fileStorage.DeleteAsync(key, cancellationToken);
            await _auditService.RecordAsync(EntityType, document.Id.ToString(), "DocumentRemoved", null, null, cancellationToken);
        }

        return FleetDocumentOperationResult.Success(Map(document));
    }

    private async Task<IReadOnlyList<FleetDocumentDto>> ListOwnedAsync(
        System.Linq.Expressions.Expression<Func<FleetDocument, bool>> ownerFilter, CancellationToken cancellationToken)
    {
        var documents = await TenantScoped()
            .AsNoTracking()
            .Where(ownerFilter)
            .OrderBy(d => d.ExpiryDate == null)
            .ThenBy(d => d.ExpiryDate)
            .ThenBy(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        return documents.Select(Map).ToList();
    }

    private static string? Validate(FleetDocumentType type, string? customTypeName, DateOnly? issueDate, DateOnly? expiryDate)
    {
        if (type == FleetDocumentType.Other && string.IsNullOrWhiteSpace(customTypeName))
        {
            return "Een naam voor het aangepaste documenttype is verplicht.";
        }

        if (issueDate is { } issue && expiryDate is { } expiry && expiry < issue)
        {
            return "De vervaldatum kan niet vóór de uitgiftedatum liggen.";
        }

        return null;
    }

    private static int? NormalizeWarningDays(int? warningDays) =>
        warningDays is { } days ? Math.Clamp(days, 0, 365) : null;

    private FleetDocumentDto Map(FleetDocument d) => new(
        d.Id, d.VehicleId, d.TrailerId, d.DocumentType, d.CustomTypeName, d.DocumentNumber,
        d.IssueDate, d.ExpiryDate, d.WarningDays,
        ComputeStatus(d.ExpiryDate, d.WarningDays, Today),
        HasAttachment: d.DocumentPath is not null,
        d.Notes, d.IssuingAuthority, d.FileName);

    public static FleetDocumentStatus ComputeStatus(DateOnly? expiryDate, int? warningDays, DateOnly today)
    {
        if (expiryDate is not { } expiry)
        {
            return FleetDocumentStatus.NoExpiry;
        }

        if (expiry < today)
        {
            return FleetDocumentStatus.Expired;
        }

        var window = warningDays ?? DefaultWarningDays;
        return expiry <= today.AddDays(window)
            ? FleetDocumentStatus.ExpiringSoon
            : FleetDocumentStatus.Valid;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
