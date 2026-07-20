using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Locations.Services;

public class LocationService : ILocationService
{
    private const string EntityType = "Location";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;

    public LocationService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICountryCodeValidator countryValidator)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
    }

    private IQueryable<Location> TenantScoped() =>
        _dbContext.Set<Location>().Where(l => l.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<LocationListItemDto>> SearchAsync(
        string? search, LocationType? type, bool? isActive, Guid? customerId,
        string? sort, string? dir, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (type is { } t) query = query.Where(l => l.Type == t);
        if (isActive is { } active) query = query.Where(l => l.IsActive == active);
        if (customerId is { } cust) query = query.Where(l => l.CustomerId == cust);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(l =>
                l.Code.ToLower().Contains(term) ||
                l.Name.ToLower().Contains(term) ||
                (l.City != null && l.City.ToLower().Contains(term)));
        }

        var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sort?.ToLowerInvariant()) switch
        {
            "code" => descending ? query.OrderByDescending(l => l.Code) : query.OrderBy(l => l.Code),
            "city" => descending ? query.OrderByDescending(l => l.City) : query.OrderBy(l => l.City),
            "type" => descending ? query.OrderByDescending(l => l.Type) : query.OrderBy(l => l.Type),
            _ => descending ? query.OrderByDescending(l => l.Name) : query.OrderBy(l => l.Name),
        };

        var projected = from l in query
                        join c in _dbContext.Customers.AsNoTracking().Where(cu => cu.TenantId == _tenantContext.TenantId)
                            on l.CustomerId equals c.Id into custs
                        from c in custs.DefaultIfEmpty()
                        select new LocationListItemDto(
                            l.Id, l.Code, l.Name, l.Type, l.City, l.CountryCode,
                            c != null ? c.Name : null, l.IsActive);

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(LocationType? type, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking().Where(l => l.IsActive);
        if (type is { } t) query = query.Where(l => l.Type == t);

        return await query
            .OrderBy(l => l.Name)
            .Select(l => new LocationOptionDto(l.Id, l.Code, l.Name, l.Type))
            .ToListAsync(cancellationToken);
    }

    public async Task<LocationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return location is null ? null : await MapToDetailAsync(location, cancellationToken);
    }

    public async Task<LocationOperationResult> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (!CoordinatesValid(request.Latitude, request.Longitude))
        {
            return LocationOperationResult.InvalidCoordinates;
        }

        if (!await CustomerInTenantAsync(request.CustomerId, cancellationToken))
        {
            return LocationOperationResult.InvalidReference;
        }

        if (await TenantScoped().AnyAsync(l => l.Code == code, cancellationToken))
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land", cancellationToken, "countryCode");

        var location = new Location
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            IsActive = true,
        };
        ApplyEditableFields(location, request.Street, request.HouseNumber, request.PostalCode, request.City, request.CountryCode,
            request.Latitude, request.Longitude, request.ContactName, request.ContactPhone, request.ContactEmail,
            request.OpeningHours, request.LoadingInstructions, request.UnloadingInstructions, request.AccessInstructions,
            request.AccessRestrictions, request.VehicleRestrictions, request.TrailerRestrictions,
            request.AlfapassRequired, request.AppointmentRequired, request.CustomerId, request.Notes);

        _dbContext.Add(location);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Created", null,
            new { location.Code, location.Name, location.Type }, cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(location, cancellationToken));
    }

    public async Task<LocationOperationResult> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return LocationOperationResult.NotFound;
        }

        var code = request.Code.Trim();
        if (!CoordinatesValid(request.Latitude, request.Longitude))
        {
            return LocationOperationResult.InvalidCoordinates;
        }

        if (!await CustomerInTenantAsync(request.CustomerId, cancellationToken))
        {
            return LocationOperationResult.InvalidReference;
        }

        if (await TenantScoped().AnyAsync(l => l.Code == code && l.Id != id, cancellationToken))
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land", cancellationToken, "countryCode");

        var before = new { location.Code, location.Name, location.Type, location.IsActive };

        location.Code = code;
        location.Name = request.Name.Trim();
        location.Type = request.Type;
        location.IsActive = request.IsActive;
        ApplyEditableFields(location, request.Street, request.HouseNumber, request.PostalCode, request.City, request.CountryCode,
            request.Latitude, request.Longitude, request.ContactName, request.ContactPhone, request.ContactEmail,
            request.OpeningHours, request.LoadingInstructions, request.UnloadingInstructions, request.AccessInstructions,
            request.AccessRestrictions, request.VehicleRestrictions, request.TrailerRestrictions,
            request.AlfapassRequired, request.AppointmentRequired, request.CustomerId, request.Notes);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Updated", before,
            new { location.Code, location.Name, location.Type, location.IsActive }, cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(location, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return false;
        }

        _dbContext.Remove(location); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Deleted",
            new { location.Code, location.Name }, null, cancellationToken);

        return true;
    }

    private static bool CoordinatesValid(decimal? lat, decimal? lng)
    {
        if (lat is { } la && (la < -90m || la > 90m)) return false;
        if (lng is { } lo && (lo < -180m || lo > 180m)) return false;
        return true;
    }

    private static void ApplyEditableFields(
        Location l, string? street, string? houseNumber, string? postalCode, string? city, string? countryCode,
        decimal? lat, decimal? lng, string? contactName, string? contactPhone, string? contactEmail,
        string? openingHours, string? loading, string? unloading, string? access,
        string? accessRestrictions, string? vehicleRestrictions, string? trailerRestrictions,
        bool alfapass, bool appointment, Guid? customerId, string? notes)
    {
        l.Street = Trim(street);
        l.HouseNumber = Trim(houseNumber);
        l.PostalCode = Trim(postalCode);
        l.City = Trim(city);
        l.CountryCode = countryCode is null ? null : Trim(countryCode)?.ToUpperInvariant();
        l.Latitude = lat;
        l.Longitude = lng;
        l.ContactName = Trim(contactName);
        l.ContactPhone = Trim(contactPhone);
        l.ContactEmail = Trim(contactEmail);
        l.OpeningHours = Trim(openingHours);
        l.LoadingInstructions = Trim(loading);
        l.UnloadingInstructions = Trim(unloading);
        l.AccessInstructions = Trim(access);
        l.AccessRestrictions = Trim(accessRestrictions);
        l.VehicleRestrictions = Trim(vehicleRestrictions);
        l.TrailerRestrictions = Trim(trailerRestrictions);
        l.AlfapassRequired = alfapass;
        l.AppointmentRequired = appointment;
        l.CustomerId = customerId;
        l.Notes = Trim(notes);
    }

    private async Task<bool> CustomerInTenantAsync(Guid? customerId, CancellationToken cancellationToken) =>
        customerId is not { } id
        || await _dbContext.Customers.AnyAsync(
            c => c.Id == id && c.TenantId == _tenantContext.TenantId, cancellationToken);

    private async Task<LocationDetailDto> MapToDetailAsync(Location l, CancellationToken cancellationToken)
    {
        string? customerName = l.CustomerId is { } cid
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == cid && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new LocationDetailDto(
            l.Id, l.Code, l.Name, l.Type,
            l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode,
            l.Latitude, l.Longitude,
            l.ContactName, l.ContactPhone, l.ContactEmail,
            l.OpeningHours, l.LoadingInstructions, l.UnloadingInstructions, l.AccessInstructions,
            l.AccessRestrictions, l.VehicleRestrictions, l.TrailerRestrictions,
            l.AlfapassRequired, l.AppointmentRequired, l.IsActive, l.CustomerId, customerName, l.Notes);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
