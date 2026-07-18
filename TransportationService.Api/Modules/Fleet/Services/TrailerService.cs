using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class TrailerService : ITrailerService
{
    private const string EntityType = "Trailer";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public TrailerService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<Trailer> TenantScoped() =>
        _dbContext.Set<Trailer>().Where(t => t.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<TrailerListItemDto>> SearchAsync(
        string? search, TrailerOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(t => t.OperationalStatus == s);
        if (isActive is { } active) query = query.Where(t => t.IsActive == active);
        if (categoryId is { } cat) query = query.Where(t => t.CategoryId == cat);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.InternalNumber, pattern) ||
                EF.Functions.Like(t.LicensePlate, pattern) ||
                (t.Brand != null && EF.Functions.Like(t.Brand, pattern)) ||
                (t.Model != null && EF.Functions.Like(t.Model, pattern)));
        }

        var projected = from t in query
                        join c in _dbContext.TrailerCategories.AsNoTracking() on t.CategoryId equals c.Id into cats
                        from c in cats.DefaultIfEmpty()
                        orderby t.InternalNumber
                        select new TrailerListItemDto(
                            t.Id, t.InternalNumber, t.LicensePlate, t.Brand, t.Model,
                            c != null ? c.Name : null, t.OperationalStatus, t.IsActive);

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<IReadOnlyList<TrailerOptionDto>> GetOptionsAsync(CancellationToken cancellationToken)
    {
        return await TenantScoped().AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.InternalNumber)
            .Select(t => new TrailerOptionDto(t.Id, t.InternalNumber, t.LicensePlate, t.Brand, t.Model))
            .ToListAsync(cancellationToken);
    }

    public async Task<TrailerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var trailer = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return trailer is null ? null : await MapToDetailAsync(trailer, cancellationToken);
    }

    public async Task<TrailerOperationResult> CreateAsync(CreateTrailerRequest request, CancellationToken cancellationToken)
    {
        var plate = request.LicensePlate.Trim().ToUpperInvariant();
        if (await TenantScoped().AnyAsync(t => t.LicensePlate == plate, cancellationToken))
        {
            return TrailerOperationResult.DuplicateLicensePlate;
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var trailer = new Trailer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            InternalNumber = GenerateInternalNumber(settings),
            LicensePlate = plate,
            IsActive = true,
            OperationalStatus = TrailerOperationalStatus.Active,
        };
        ApplyEditableFields(trailer, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.CapacityKg, request.LengthMeters, request.WidthMeters, request.HeightMeters,
            request.VolumeM3, request.HasRefrigeration, request.AdrSuitable, request.OwnershipType, request.Notes);

        _dbContext.Add(trailer);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return TrailerOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, trailer.Id.ToString(), "Created", null,
            new { trailer.InternalNumber, trailer.LicensePlate }, cancellationToken);

        return TrailerOperationResult.Success(await MapToDetailAsync(trailer, cancellationToken));
    }

    public async Task<TrailerOperationResult> UpdateAsync(Guid id, UpdateTrailerRequest request, CancellationToken cancellationToken)
    {
        var trailer = await TenantScoped().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trailer is null)
        {
            return TrailerOperationResult.NotFound;
        }

        var plate = request.LicensePlate.Trim().ToUpperInvariant();
        if (await TenantScoped().AnyAsync(t => t.LicensePlate == plate && t.Id != id, cancellationToken))
        {
            return TrailerOperationResult.DuplicateLicensePlate;
        }

        var before = new { trailer.LicensePlate, trailer.OperationalStatus, trailer.IsActive };

        trailer.LicensePlate = plate;
        trailer.OperationalStatus = request.OperationalStatus;
        trailer.IsActive = request.IsActive;
        ApplyEditableFields(trailer, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.CapacityKg, request.LengthMeters, request.WidthMeters, request.HeightMeters,
            request.VolumeM3, request.HasRefrigeration, request.AdrSuitable, request.OwnershipType, request.Notes);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return TrailerOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, trailer.Id.ToString(), "Updated", before,
            new { trailer.LicensePlate, trailer.OperationalStatus, trailer.IsActive }, cancellationToken);

        return TrailerOperationResult.Success(await MapToDetailAsync(trailer, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var trailer = await TenantScoped().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trailer is null)
        {
            return false;
        }

        _dbContext.Remove(trailer); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trailer.Id.ToString(), "Deleted",
            new { trailer.InternalNumber, trailer.LicensePlate }, null, cancellationToken);

        return true;
    }

    private static void ApplyEditableFields(
        Trailer t, string? vin, Guid? categoryId, string? brand, string? model, int? year,
        DateOnly? firstRegistration, decimal? capacity, decimal? length, decimal? width, decimal? height, decimal? volume,
        bool refrigeration, bool adr, VehicleOwnershipType ownership, string? notes)
    {
        t.Vin = Trim(vin);
        t.CategoryId = categoryId;
        t.Brand = Trim(brand);
        t.Model = Trim(model);
        t.Year = year;
        t.FirstRegistrationDate = firstRegistration;
        t.CapacityKg = capacity;
        t.LengthMeters = length;
        t.WidthMeters = width;
        t.HeightMeters = height;
        t.VolumeM3 = volume;
        t.HasRefrigeration = refrigeration;
        t.AdrSuitable = adr;
        t.OwnershipType = ownership;
        t.Notes = Trim(notes);
    }

    private async Task<TrailerDetailDto> MapToDetailAsync(Trailer t, CancellationToken cancellationToken)
    {
        var categoryName = t.CategoryId is { } catId
            ? await _dbContext.TrailerCategories.AsNoTracking().Where(c => c.Id == catId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new TrailerDetailDto(
            t.Id, t.InternalNumber, t.LicensePlate, t.Vin, t.CategoryId, categoryName, t.Brand, t.Model, t.Year, t.FirstRegistrationDate,
            t.CapacityKg, t.LengthMeters, t.WidthMeters, t.HeightMeters, t.VolumeM3,
            t.HasRefrigeration, t.AdrSuitable, t.OwnershipType, t.OperationalStatus, t.IsActive, t.Notes);
    }

    private static string GenerateInternalNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"OPL-{Guid.NewGuid().ToString("N")[..8]}";
        }

        var prefix = settings.TrailerNumberPrefix ?? "OPL-";
        var number = $"{prefix}{settings.TrailerNumberNextValue:D4}";
        settings.TrailerNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
