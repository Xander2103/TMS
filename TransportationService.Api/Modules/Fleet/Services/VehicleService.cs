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

public class VehicleService : IVehicleService
{
    private const string EntityType = "Vehicle";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public VehicleService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<Vehicle> TenantScoped() =>
        _dbContext.Set<Vehicle>().Where(v => v.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<VehicleListItemDto>> SearchAsync(
        string? search, VehicleOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(v => v.OperationalStatus == s);
        if (isActive is { } active) query = query.Where(v => v.IsActive == active);
        if (categoryId is { } cat) query = query.Where(v => v.CategoryId == cat);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.InternalNumber.ToLower().Contains(term) ||
                v.LicensePlate.ToLower().Contains(term) ||
                (v.Brand != null && v.Brand.ToLower().Contains(term)) ||
                (v.Model != null && v.Model.ToLower().Contains(term)));
        }

        var projected = from v in query
                        join c in _dbContext.VehicleCategories.AsNoTracking().Where(vc => vc.TenantId == _tenantContext.TenantId)
                            on v.CategoryId equals c.Id into cats
                        from c in cats.DefaultIfEmpty()
                        orderby v.InternalNumber
                        select new VehicleListItemDto(
                            v.Id, v.InternalNumber, v.LicensePlate, v.Brand, v.Model,
                            c != null ? c.Name : null, v.OperationalStatus, v.IsActive);

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(CancellationToken cancellationToken)
    {
        return await TenantScoped().AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.InternalNumber)
            .Select(v => new VehicleOptionDto(v.Id, v.InternalNumber, v.LicensePlate, v.Brand, v.Model))
            .ToListAsync(cancellationToken);
    }

    public async Task<VehicleDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        return vehicle is null ? null : await MapToDetailAsync(vehicle, cancellationToken);
    }

    public async Task<VehicleOperationResult> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken)
    {
        var plate = request.LicensePlate.Trim().ToUpperInvariant();
        if (await TenantScoped().AnyAsync(v => v.LicensePlate == plate, cancellationToken))
        {
            return VehicleOperationResult.DuplicateLicensePlate;
        }

        if (!await ReferencesInTenantAsync(request.CategoryId, request.FixedDriverId, request.CurrentDriverId, cancellationToken))
        {
            return VehicleOperationResult.InvalidReference;
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            LicensePlate = plate,
            IsActive = true,
            OperationalStatus = VehicleOperationalStatus.Active,
        };
        ApplyEditableFields(vehicle, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.FuelType, request.EmissionClass,
            request.GrossVehicleWeightKg, request.PayloadKg, request.LengthMeters, request.WidthMeters, request.HeightMeters, request.VolumeM3,
            request.OdometerKm, request.HasCrane, request.HasRefrigeration, request.HasTailLift, request.AdrSuitable,
            request.OwnershipType, request.FixedDriverId, request.CurrentDriverId, request.Notes);

        _dbContext.Add(vehicle);
        try
        {
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () => vehicle.InternalNumber = GenerateInternalNumber(settings),
                cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            return VehicleOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Created", null,
            new { vehicle.InternalNumber, vehicle.LicensePlate }, cancellationToken);

        return VehicleOperationResult.Success(await MapToDetailAsync(vehicle, cancellationToken));
    }

    public async Task<VehicleOperationResult> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return VehicleOperationResult.NotFound;
        }

        var plate = request.LicensePlate.Trim().ToUpperInvariant();
        if (await TenantScoped().AnyAsync(v => v.LicensePlate == plate && v.Id != id, cancellationToken))
        {
            return VehicleOperationResult.DuplicateLicensePlate;
        }

        if (!await ReferencesInTenantAsync(request.CategoryId, request.FixedDriverId, request.CurrentDriverId, cancellationToken))
        {
            return VehicleOperationResult.InvalidReference;
        }

        var before = new { vehicle.LicensePlate, vehicle.OperationalStatus, vehicle.IsActive };

        vehicle.LicensePlate = plate;
        vehicle.OperationalStatus = request.OperationalStatus;
        vehicle.IsActive = request.IsActive;
        ApplyEditableFields(vehicle, request.Vin, request.CategoryId, request.Brand, request.Model, request.Year,
            request.FirstRegistrationDate, request.FuelType, request.EmissionClass,
            request.GrossVehicleWeightKg, request.PayloadKg, request.LengthMeters, request.WidthMeters, request.HeightMeters, request.VolumeM3,
            request.OdometerKm, request.HasCrane, request.HasRefrigeration, request.HasTailLift, request.AdrSuitable,
            request.OwnershipType, request.FixedDriverId, request.CurrentDriverId, request.Notes);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return VehicleOperationResult.DuplicateLicensePlate;
        }

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Updated", before,
            new { vehicle.LicensePlate, vehicle.OperationalStatus, vehicle.IsActive }, cancellationToken);

        return VehicleOperationResult.Success(await MapToDetailAsync(vehicle, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await TenantScoped().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        _dbContext.Remove(vehicle); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, vehicle.Id.ToString(), "Deleted",
            new { vehicle.InternalNumber, vehicle.LicensePlate }, null, cancellationToken);

        return true;
    }

    private static void ApplyEditableFields(
        Vehicle v, string? vin, Guid? categoryId, string? brand, string? model, int? year,
        DateOnly? firstRegistration, FuelType fuelType, EmissionClass? emissionClass,
        decimal? grossWeight, decimal? payload, decimal? length, decimal? width, decimal? height, decimal? volume,
        int odometer, bool crane, bool refrigeration, bool tailLift, bool adr,
        VehicleOwnershipType ownership, Guid? fixedDriverId, Guid? currentDriverId, string? notes)
    {
        v.Vin = Trim(vin);
        v.CategoryId = categoryId;
        v.Brand = Trim(brand);
        v.Model = Trim(model);
        v.Year = year;
        v.FirstRegistrationDate = firstRegistration;
        v.FuelType = fuelType;
        v.EmissionClass = emissionClass;
        v.GrossVehicleWeightKg = grossWeight;
        v.PayloadKg = payload;
        v.LengthMeters = length;
        v.WidthMeters = width;
        v.HeightMeters = height;
        v.VolumeM3 = volume;
        v.OdometerKm = odometer < 0 ? 0 : odometer;
        v.HasCrane = crane;
        v.HasRefrigeration = refrigeration;
        v.HasTailLift = tailLift;
        v.AdrSuitable = adr;
        v.OwnershipType = ownership;
        v.FixedDriverId = fixedDriverId;
        v.CurrentDriverId = currentDriverId;
        v.Notes = Trim(notes);
    }

    /// <summary>All optional references on a vehicle must resolve within the current tenant.</summary>
    private async Task<bool> ReferencesInTenantAsync(
        Guid? categoryId, Guid? fixedDriverId, Guid? currentDriverId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (categoryId is { } cat && !await _dbContext.VehicleCategories
                .AnyAsync(c => c.Id == cat && c.TenantId == tenantId, cancellationToken))
        {
            return false;
        }

        foreach (var driverId in new[] { fixedDriverId, currentDriverId })
        {
            if (driverId is { } d && !await _dbContext.Drivers
                    .AnyAsync(x => x.Id == d && x.TenantId == tenantId, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<VehicleDetailDto> MapToDetailAsync(Vehicle v, CancellationToken cancellationToken)
    {
        var categoryName = v.CategoryId is { } catId
            ? await _dbContext.VehicleCategories.AsNoTracking()
                .Where(c => c.Id == catId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var fixedDriverName = await ResolveDriverNameAsync(v.FixedDriverId, cancellationToken);
        var currentDriverName = await ResolveDriverNameAsync(v.CurrentDriverId, cancellationToken);

        return new VehicleDetailDto(
            v.Id, v.InternalNumber, v.LicensePlate, v.Vin, v.CategoryId, categoryName, v.Brand, v.Model, v.Year, v.FirstRegistrationDate,
            v.FuelType, v.EmissionClass, v.GrossVehicleWeightKg, v.PayloadKg, v.LengthMeters, v.WidthMeters, v.HeightMeters, v.VolumeM3,
            v.OdometerKm, v.HasCrane, v.HasRefrigeration, v.HasTailLift, v.AdrSuitable,
            v.OwnershipType, v.OperationalStatus, v.IsActive,
            v.FixedDriverId, fixedDriverName, v.CurrentDriverId, currentDriverName, v.Notes);
    }

    private async Task<string?> ResolveDriverNameAsync(Guid? driverId, CancellationToken cancellationToken)
    {
        if (driverId is not { } id)
        {
            return null;
        }

        return await _dbContext.Set<TransportationService.Api.Modules.Drivers.Entities.Driver>().AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == _tenantContext.TenantId)
            .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string GenerateInternalNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"VRT-{Guid.NewGuid().ToString("N")[..8]}";
        }

        var prefix = settings.VehicleNumberPrefix ?? "VRT-";
        var number = $"{prefix}{settings.VehicleNumberNextValue:D4}";
        settings.VehicleNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
