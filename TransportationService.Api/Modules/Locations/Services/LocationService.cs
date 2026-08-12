using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Entities;

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
        string? country, string? postalCode,
        string? sort, string? dir, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (type is { } t) query = query.Where(l => l.Type == t);
        if (isActive is { } active) query = query.Where(l => l.IsActive == active);
        if (customerId is { } cust) query = query.Where(l => l.CustomerId == cust);
        if (!string.IsNullOrWhiteSpace(country))
        {
            var cc = country.Trim().ToUpperInvariant();
            query = query.Where(l => l.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            var pc = postalCode.Trim().ToLowerInvariant();
            query = query.Where(l => l.PostalCode != null && l.PostalCode.ToLower().StartsWith(pc));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(l =>
                l.Code.ToLower().Contains(term) ||
                l.Name.ToLower().Contains(term) ||
                (l.City != null && l.City.ToLower().Contains(term)) ||
                (l.PostalCode != null && l.PostalCode.ToLower().Contains(term)));
        }

        // Join BEFORE ordering so the customer column is a first-class, server-side sort key.
        var joined = from l in query
                     join c in _dbContext.Customers.AsNoTracking().Where(cu => cu.TenantId == _tenantContext.TenantId)
                         on l.CustomerId equals c.Id into custs
                     from c in custs.DefaultIfEmpty()
                     select new { Location = l, CustomerName = c != null ? (string?)c.Name : null };

        var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        joined = (sort?.ToLowerInvariant()) switch
        {
            "code" => descending ? joined.OrderByDescending(x => x.Location.Code) : joined.OrderBy(x => x.Location.Code),
            "city" => descending ? joined.OrderByDescending(x => x.Location.City) : joined.OrderBy(x => x.Location.City),
            "type" => descending ? joined.OrderByDescending(x => x.Location.Type) : joined.OrderBy(x => x.Location.Type),
            "customer" => descending
                ? joined.OrderByDescending(x => x.CustomerName).ThenBy(x => x.Location.Name)
                : joined.OrderBy(x => x.CustomerName).ThenBy(x => x.Location.Name),
            "status" => descending
                ? joined.OrderByDescending(x => x.Location.IsActive).ThenBy(x => x.Location.Name)
                : joined.OrderBy(x => x.Location.IsActive).ThenBy(x => x.Location.Name),
            _ => descending ? joined.OrderByDescending(x => x.Location.Name) : joined.OrderBy(x => x.Location.Name),
        };

        var projected = joined.Select(x => new LocationListItemDto(
            x.Location.Id, x.Location.Code, x.Location.Name, x.Location.Type, x.Location.City,
            x.Location.CountryCode, x.CustomerName, x.Location.IsActive,
            x.Location.IsDefaultLoadingLocation, x.Location.IsDefaultUnloadingLocation,
            x.Location.IsDefaultBillingLocation));

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    /// <summary>
    /// Locations-UX wave: the per-customer view. Server-side grouping with HONEST paging —
    /// the page unit is the GROUP (customer, or the unlinked bucket), never a slice of
    /// locations that would tear a customer across pages. All Search filters apply first;
    /// groups order by customer name with the unlinked bucket last; locations within a group
    /// order by the requested inner sort (name | code | city).
    /// </summary>
    public async Task<PagedResult<LocationGroupDto>> SearchGroupedAsync(
        string? search, LocationType? type, bool? isActive, Guid? customerId,
        string? country, string? postalCode, string? innerSort,
        PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();
        if (type is { } t) query = query.Where(l => l.Type == t);
        if (isActive is { } active) query = query.Where(l => l.IsActive == active);
        if (customerId is { } cust) query = query.Where(l => l.CustomerId == cust);
        if (!string.IsNullOrWhiteSpace(country))
        {
            var cc = country.Trim().ToUpperInvariant();
            query = query.Where(l => l.CountryCode == cc);
        }

        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            var pc = postalCode.Trim().ToLowerInvariant();
            query = query.Where(l => l.PostalCode != null && l.PostalCode.ToLower().StartsWith(pc));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(l =>
                l.Code.ToLower().Contains(term) ||
                l.Name.ToLower().Contains(term) ||
                (l.City != null && l.City.ToLower().Contains(term)) ||
                (l.PostalCode != null && l.PostalCode.ToLower().Contains(term)));
        }

        var joined = from l in query
                     join c in _dbContext.Customers.AsNoTracking().Where(cu => cu.TenantId == _tenantContext.TenantId)
                         on l.CustomerId equals c.Id into custs
                     from c in custs.DefaultIfEmpty()
                     select new { Location = l, CustomerName = c != null ? (string?)c.Name : null };

        // Page over the distinct groups (customer name, unlinked last), then load ONLY the
        // locations of the paged groups — two queries, correct totals, no client-side slicing.
        var groupKeys = joined
            .Select(x => new { x.Location.CustomerId, x.CustomerName })
            .Distinct()
            .OrderBy(g => g.CustomerName == null)
            .ThenBy(g => g.CustomerName);
        var totalGroups = await groupKeys.CountAsync(cancellationToken);
        var pagedKeys = await groupKeys
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        var pagedCustomerIds = pagedKeys.Where(k => k.CustomerId is not null).Select(k => k.CustomerId).ToList();
        var includeUnlinked = pagedKeys.Any(k => k.CustomerId is null);
        var rows = await joined
            .Where(x => (x.Location.CustomerId != null && pagedCustomerIds.Contains(x.Location.CustomerId))
                        || (includeUnlinked && x.Location.CustomerId == null))
            .Select(x => new
            {
                x.Location.CustomerId, x.CustomerName,
                Dto = new LocationListItemDto(
                    x.Location.Id, x.Location.Code, x.Location.Name, x.Location.Type, x.Location.City,
                    x.Location.CountryCode, x.CustomerName, x.Location.IsActive,
                    x.Location.IsDefaultLoadingLocation, x.Location.IsDefaultUnloadingLocation,
                    x.Location.IsDefaultBillingLocation),
            })
            .ToListAsync(cancellationToken);

        IOrderedEnumerable<LocationListItemDto> InnerOrder(IEnumerable<LocationListItemDto> items) =>
            (innerSort?.ToLowerInvariant()) switch
            {
                "code" => items.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase),
                "city" => items.OrderBy(i => i.City, StringComparer.OrdinalIgnoreCase),
                _ => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            };

        var groups = pagedKeys
            .Select(key => new LocationGroupDto(
                key.CustomerId, key.CustomerName,
                InnerOrder(rows.Where(r => r.CustomerId == key.CustomerId).Select(r => r.Dto)).ToList()))
            .ToList();

        return new PagedResult<LocationGroupDto>(groups, totalGroups, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(
        LocationType? type, Guid? customerId, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking().Where(l => l.IsActive);
        if (type is { } t) query = query.Where(l => l.Type == t);
        // Customer scope = the customer's own sites plus company-wide (unlinked) locations;
        // other customers' sites are never offered.
        if (customerId is { } cust) query = query.Where(l => l.CustomerId == cust || l.CustomerId == null);

        return await query
            .OrderByDescending(l => l.IsDefaultLoadingLocation || l.IsDefaultUnloadingLocation)
            .ThenBy(l => l.Name)
            .Select(l => new LocationOptionDto(
                l.Id, l.Code, l.Name, l.Type, l.City,
                l.IsDefaultLoadingLocation, l.IsDefaultUnloadingLocation, l.IsDefaultBillingLocation,
                // Street + house number as one display line ("Noorderlaan 10"); null when both empty.
                (l.Street ?? "") == "" && (l.HouseNumber ?? "") == ""
                    ? null
                    : ((l.Street ?? "") + " " + (l.HouseNumber ?? "")).Trim(),
                l.PostalCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<LocationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken, bool canViewSensitive = false)
    {
        var location = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return location is null ? null : await MapToDetailAsync(location, cancellationToken, canViewSensitive);
    }

    public async Task<LocationOperationResult> CreateAsync(
        CreateLocationRequest request, CancellationToken cancellationToken, bool canViewSensitive = false)
    {
        // Code is optional since the master-data wave: blank → generated "LOC-xxxxxxxx"
        // (the DB column stays required; a generated-code race is retried once below).
        var explicitCode = Trim(request.Code);
        var codeGenerated = explicitCode is null;
        var code = explicitCode ?? GenerateCode();

        if (!CoordinatesValid(request.Latitude, request.Longitude))
        {
            return LocationOperationResult.InvalidCoordinates;
        }

        if (!await CustomerInTenantAsync(request.CustomerId, cancellationToken))
        {
            return LocationOperationResult.InvalidReference;
        }

        await EnsureCustomerContactValidAsync(request.CustomerContactId, request.CustomerId, cancellationToken);

        if (!codeGenerated && await TenantScoped().AnyAsync(l => l.Code == code, cancellationToken))
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
        ApplyEditableFields(location, ToEditableShape(request), canViewSensitive);
        location.OpeningIntervals = BuildIntervals(request.OpeningIntervals, location);

        // Transaction: default-demotion runs as an immediate UPDATE and must roll back when the
        // insert itself fails.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ApplyDefaultFlagsAsync(location, request.IsDefaultLoadingLocation, request.IsDefaultUnloadingLocation, request.IsDefaultBillingLocation, cancellationToken);

        _dbContext.Add(location);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!codeGenerated)
            {
                return LocationOperationResult.DuplicateCode;
            }

            // Generated-code collision on the unique index: retry exactly once with a new code.
            location.Code = GenerateCode();
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return LocationOperationResult.DuplicateCode;
            }
        }

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Created", null,
            await BuildAuditPayloadAsync(location, cancellationToken), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(location, cancellationToken, canViewSensitive));
    }

    public async Task<LocationOperationResult> UpdateAsync(
        Guid id, UpdateLocationRequest request, CancellationToken cancellationToken, bool canViewSensitive = false)
    {
        var location = await TenantScoped()
            .Include(l => l.OpeningIntervals)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
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

        await EnsureCustomerContactValidAsync(request.CustomerContactId, request.CustomerId, cancellationToken);

        if (await TenantScoped().AnyAsync(l => l.Code == code && l.Id != id, cancellationToken))
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land", cancellationToken, "countryCode");

        var before = await BuildAuditPayloadAsync(location, cancellationToken);

        location.Code = code;
        location.Name = request.Name.Trim();
        location.Type = request.Type;
        location.IsActive = request.IsActive;
        ApplyEditableFields(location, request, canViewSensitive);

        // Opening intervals are replaced wholesale: nothing outside the location references an
        // individual interval row, so id preservation buys nothing (documented on the entity).
        var newIntervals = BuildIntervals(request.OpeningIntervals, location);
        _dbContext.RemoveRange(location.OpeningIntervals);
        _dbContext.AddRange(newIntervals);
        location.OpeningIntervals = newIntervals;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ApplyDefaultFlagsAsync(location, request.IsDefaultLoadingLocation, request.IsDefaultUnloadingLocation, request.IsDefaultBillingLocation, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LocationOperationResult.DuplicateCode;
        }

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Updated", before,
            await BuildAuditPayloadAsync(location, cancellationToken), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(location, cancellationToken, canViewSensitive));
    }

    public async Task<bool> SetActiveAsync(Guid id, SetLocationActiveRequest request, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return false;
        }

        if (location.IsActive != request.IsActive)
        {
            location.IsActive = request.IsActive;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, location.Id.ToString(),
                request.IsActive ? "Activated" : "Deactivated", null,
                new { location.IsActive }, cancellationToken);
        }

        return true;
    }

    public async Task<LocationOperationResult> SetDefaultsAsync(
        Guid id, SetLocationDefaultsRequest request, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return LocationOperationResult.NotFound;
        }

        var before = new { location.IsDefaultLoadingLocation, location.IsDefaultUnloadingLocation, location.IsDefaultBillingLocation };
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ApplyDefaultFlagsAsync(location, request.IsDefaultLoadingLocation, request.IsDefaultUnloadingLocation, request.IsDefaultBillingLocation, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "DefaultsChanged", before,
            new { location.IsDefaultLoadingLocation, location.IsDefaultUnloadingLocation, location.IsDefaultBillingLocation }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(location, cancellationToken));
    }

    /// <summary>
    /// Applies the default-loading/unloading flags. Defaults require a linked customer, and at
    /// most one location per customer may hold each flag. Demotion of the previous holder runs
    /// as an immediate UPDATE — the caller wraps promote + demote in one transaction, because a
    /// single SaveChanges batch can order the promote before the demote and trip the partial
    /// unique index.
    /// </summary>
    private async Task ApplyDefaultFlagsAsync(
        Location location, bool isDefaultLoading, bool isDefaultUnloading, bool isDefaultBilling, CancellationToken cancellationToken)
    {
        if ((isDefaultLoading || isDefaultUnloading || isDefaultBilling) && location.CustomerId is null)
        {
            throw new DomainValidationException("isDefaultLoadingLocation",
                "Alleen locaties die aan een klant gekoppeld zijn, kunnen als standaard laad-, los- of facturatieadres dienen.");
        }

        if (location.CustomerId is { } customerId && (isDefaultLoading || isDefaultUnloading || isDefaultBilling))
        {
            await TenantScoped()
                .Where(l => l.CustomerId == customerId && l.Id != location.Id
                    && ((isDefaultLoading && l.IsDefaultLoadingLocation)
                        || (isDefaultUnloading && l.IsDefaultUnloadingLocation)
                        || (isDefaultBilling && l.IsDefaultBillingLocation)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.IsDefaultLoadingLocation, l => !isDefaultLoading && l.IsDefaultLoadingLocation)
                    .SetProperty(l => l.IsDefaultUnloadingLocation, l => !isDefaultUnloading && l.IsDefaultUnloadingLocation)
                    .SetProperty(l => l.IsDefaultBillingLocation, l => !isDefaultBilling && l.IsDefaultBillingLocation),
                    cancellationToken);
        }

        location.IsDefaultLoadingLocation = isDefaultLoading;
        location.IsDefaultUnloadingLocation = isDefaultUnloading;
        location.IsDefaultBillingLocation = isDefaultBilling;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await TenantScoped().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return false;
        }

        // A location that has ever been used must be deactivated, never deleted. The checks use
        // IgnoreQueryFilters + an explicit TenantId predicate: IgnoreQueryFilters bypasses the
        // tenant AND soft-delete filters, and soft-deleted stops still belong to a live order
        // (stops are rebuilt wholesale on every order save).
        var tenantId = _tenantContext.TenantId;
        var referenced =
            await _dbContext.Set<TransportOrderStop>().IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenantId && s.LocationId == id, cancellationToken)
            || await _dbContext.Set<Warehouse>().IgnoreQueryFilters()
                .AnyAsync(w => w.TenantId == tenantId && w.LocationId == id, cancellationToken)
            || await _dbContext.Set<EdiPartnerLocation>().IgnoreQueryFilters()
                .AnyAsync(m => m.TenantId == tenantId && m.LocationId == id, cancellationToken);
        if (referenced)
        {
            throw new DomainValidationException(
                "Deze locatie is al gebruikt en kan niet worden verwijderd. Je kunt de locatie wel deactiveren.");
        }

        _dbContext.Remove(location); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, location.Id.ToString(), "Deleted",
            new { location.Code, location.Name }, null, cancellationToken);

        return true;
    }

    public async Task<LocationOperationResult> DuplicateAsync(
        Guid id, CancellationToken cancellationToken, bool canViewSensitive = false)
    {
        var source = await TenantScoped().AsNoTracking()
            .Include(l => l.OpeningIntervals)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (source is null)
        {
            return LocationOperationResult.NotFound;
        }

        var copy = new Location
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Code = GenerateCode(),
            Name = CopyName(source.Name),
            Type = source.Type,
            Street = source.Street,
            HouseNumber = source.HouseNumber,
            PostalCode = source.PostalCode,
            City = source.City,
            CountryCode = source.CountryCode,
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            ContactName = source.ContactName,
            ContactPhone = source.ContactPhone,
            ContactEmail = source.ContactEmail,
            ContactMobile = source.ContactMobile,
            CustomerContactId = source.CustomerContactId,
            ExternalReference = source.ExternalReference,
            OpeningHours = source.OpeningHours,
            LoadingInstructions = source.LoadingInstructions,
            UnloadingInstructions = source.UnloadingInstructions,
            AccessInstructions = source.AccessInstructions,
            AccessRestrictions = source.AccessRestrictions,
            VehicleRestrictions = source.VehicleRestrictions,
            TrailerRestrictions = source.TrailerRestrictions,
            AlfapassRequired = source.AlfapassRequired,
            AppointmentRequired = source.AppointmentRequired,
            Gate = source.Gate,
            AccessCode = source.AccessCode,
            ReceptionPoint = source.ReceptionPoint,
            Dock = source.Dock,
            RouteDescription = source.RouteDescription,
            DeliveryByAppointmentOnly = source.DeliveryByAppointmentOnly,
            HeightRestrictionMeters = source.HeightRestrictionMeters,
            WeightRestrictionTons = source.WeightRestrictionTons,
            AdrAllowed = source.AdrAllowed,
            CraneRequired = source.CraneRequired,
            ForkliftAvailable = source.ForkliftAvailable,
            DriverInstructions = source.DriverInstructions,
            InternalMemo = source.InternalMemo,
            DefaultLoadingMinutes = source.DefaultLoadingMinutes,
            DefaultUnloadingMinutes = source.DefaultUnloadingMinutes,
            PreferredArrivalFrom = source.PreferredArrivalFrom,
            PreferredArrivalTo = source.PreferredArrivalTo,
            EarliestArrival = source.EarliestArrival,
            LatestArrival = source.LatestArrival,
            CustomerId = source.CustomerId,
            Notes = source.Notes,
            IsActive = true,
            IsDefaultLoadingLocation = false,
            IsDefaultUnloadingLocation = false,
            IsDefaultBillingLocation = false,
        };
        copy.OpeningIntervals = source.OpeningIntervals
            .Select(i => new LocationOpeningInterval
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                LocationId = copy.Id,
                DayOfWeek = i.DayOfWeek,
                FromTime = i.FromTime,
                ToTime = i.ToTime,
                Note = i.Note,
            })
            .ToList();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.Add(copy);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Generated-code collision: retry exactly once with a new code.
            copy.Code = GenerateCode();
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return LocationOperationResult.DuplicateCode;
            }
        }

        await _auditService.RecordAsync(EntityType, copy.Id.ToString(), "Duplicated", null,
            new { SourceLocationId = id, copy.Code, copy.Name }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LocationOperationResult.Success(await MapToDetailAsync(copy, cancellationToken, canViewSensitive));
    }

    private static string CopyName(string sourceName)
    {
        const string suffix = " (kopie)";
        var name = sourceName + suffix;
        return name.Length <= 200 ? name : sourceName[..(200 - suffix.Length)] + suffix;
    }

    private static string GenerateCode() => "LOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static bool CoordinatesValid(decimal? lat, decimal? lng)
    {
        if (lat is { } la && (la < -90m || la > 90m)) return false;
        if (lng is { } lo && (lo < -180m || lo > 180m)) return false;
        return true;
    }

    /// <summary>Create and update requests share the editable-field shape; the update record is that shape.</summary>
    private static UpdateLocationRequest ToEditableShape(CreateLocationRequest r) => new(
        r.Code ?? string.Empty, r.Name, r.Type, r.Street, r.HouseNumber, r.PostalCode, r.City, r.CountryCode,
        r.Latitude, r.Longitude, r.ContactName, r.ContactPhone, r.ContactEmail,
        r.OpeningHours, r.LoadingInstructions, r.UnloadingInstructions, r.AccessInstructions,
        r.AccessRestrictions, r.VehicleRestrictions, r.TrailerRestrictions,
        r.AlfapassRequired, r.AppointmentRequired, IsActive: true, r.CustomerId, r.Notes,
        r.IsDefaultLoadingLocation, r.IsDefaultUnloadingLocation, r.IsDefaultBillingLocation,
        r.ExternalReference, r.ContactMobile, r.CustomerContactId, r.Gate, r.AccessCode,
        r.ReceptionPoint, r.Dock, r.RouteDescription, r.DeliveryByAppointmentOnly,
        r.HeightRestrictionMeters, r.WeightRestrictionTons, r.AdrAllowed, r.CraneRequired,
        r.ForkliftAvailable, r.DriverInstructions, r.InternalMemo,
        r.DefaultLoadingMinutes, r.DefaultUnloadingMinutes,
        r.PreferredArrivalFrom, r.PreferredArrivalTo, r.EarliestArrival, r.LatestArrival,
        r.OpeningIntervals);

    private static void ApplyEditableFields(Location l, UpdateLocationRequest r, bool canEditSensitive)
    {
        l.Street = Trim(r.Street);
        l.HouseNumber = Trim(r.HouseNumber);
        l.PostalCode = Trim(r.PostalCode);
        l.City = Trim(r.City);
        l.CountryCode = r.CountryCode is null ? null : Trim(r.CountryCode)?.ToUpperInvariant();
        l.Latitude = r.Latitude;
        l.Longitude = r.Longitude;
        l.ContactName = Trim(r.ContactName);
        l.ContactPhone = Trim(r.ContactPhone);
        l.ContactEmail = Trim(r.ContactEmail);
        l.ContactMobile = Trim(r.ContactMobile);
        l.CustomerContactId = r.CustomerContactId;
        l.ExternalReference = Trim(r.ExternalReference);
        l.OpeningHours = Trim(r.OpeningHours);
        l.LoadingInstructions = Trim(r.LoadingInstructions);
        l.UnloadingInstructions = Trim(r.UnloadingInstructions);
        l.AccessInstructions = Trim(r.AccessInstructions);
        l.AccessRestrictions = Trim(r.AccessRestrictions);
        l.VehicleRestrictions = Trim(r.VehicleRestrictions);
        l.TrailerRestrictions = Trim(r.TrailerRestrictions);
        l.AlfapassRequired = r.AlfapassRequired;
        l.AppointmentRequired = r.AppointmentRequired;
        l.CustomerId = r.CustomerId;
        l.Notes = Trim(r.Notes);

        l.Gate = Trim(r.Gate);
        if (canEditSensitive)
        {
            // Without locations.view_sensitive the stored code is preserved untouched (mirrors
            // the employees confidential-field pattern); on create the field simply stays null.
            l.AccessCode = Trim(r.AccessCode);
        }

        l.ReceptionPoint = Trim(r.ReceptionPoint);
        l.Dock = Trim(r.Dock);
        l.RouteDescription = Trim(r.RouteDescription);
        l.DeliveryByAppointmentOnly = r.DeliveryByAppointmentOnly;
        l.HeightRestrictionMeters = r.HeightRestrictionMeters;
        l.WeightRestrictionTons = r.WeightRestrictionTons;
        l.AdrAllowed = r.AdrAllowed;
        l.CraneRequired = r.CraneRequired;
        l.ForkliftAvailable = r.ForkliftAvailable;
        l.DriverInstructions = Trim(r.DriverInstructions);
        l.InternalMemo = Trim(r.InternalMemo);

        ValidateMinutes(r.DefaultLoadingMinutes, "defaultLoadingMinutes");
        ValidateMinutes(r.DefaultUnloadingMinutes, "defaultUnloadingMinutes");
        l.DefaultLoadingMinutes = r.DefaultLoadingMinutes;
        l.DefaultUnloadingMinutes = r.DefaultUnloadingMinutes;

        l.PreferredArrivalFrom = ParseOptionalTime(r.PreferredArrivalFrom, "preferredArrivalFrom");
        l.PreferredArrivalTo = ParseOptionalTime(r.PreferredArrivalTo, "preferredArrivalTo");
        l.EarliestArrival = ParseOptionalTime(r.EarliestArrival, "earliestArrival");
        l.LatestArrival = ParseOptionalTime(r.LatestArrival, "latestArrival");
    }

    private static void ValidateMinutes(int? value, string field)
    {
        if (value is < 0 or > 1440)
        {
            throw new DomainValidationException(field, "De waarde moet tussen 0 en 1440 minuten liggen.");
        }
    }

    private static TimeOnly? ParseOptionalTime(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseTime(value, field);

    private static TimeOnly ParseTime(string value, string field)
    {
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new DomainValidationException(field, "Ongeldige tijd (gebruik uu:mm, bv. 07:30).");
        }

        return time;
    }

    private List<LocationOpeningInterval> BuildIntervals(
        IReadOnlyList<LocationOpeningIntervalDto>? dtos, Location location)
    {
        var result = new List<LocationOpeningInterval>();
        if (dtos is null || dtos.Count == 0)
        {
            return result;
        }

        for (var i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            if (dto.DayOfWeek is < 1 or > 7)
            {
                throw new DomainValidationException($"openingIntervals[{i}].dayOfWeek",
                    "De dag moet tussen 1 (maandag) en 7 (zondag) liggen.");
            }

            var from = ParseTime(dto.FromTime, $"openingIntervals[{i}].fromTime");
            var to = ParseTime(dto.ToTime, $"openingIntervals[{i}].toTime");
            if (from >= to)
            {
                throw new DomainValidationException($"openingIntervals[{i}].toTime",
                    "De eindtijd moet na de starttijd liggen.");
            }

            result.Add(new LocationOpeningInterval
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                LocationId = location.Id,
                DayOfWeek = dto.DayOfWeek,
                FromTime = from,
                ToTime = to,
                Note = Trim(dto.Note),
            });
        }

        for (var i = 0; i < result.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (result[i].DayOfWeek == result[j].DayOfWeek
                    && result[i].FromTime < result[j].ToTime
                    && result[j].FromTime < result[i].ToTime)
                {
                    throw new DomainValidationException($"openingIntervals[{i}].fromTime",
                        "De tijdvakken van eenzelfde dag mogen niet overlappen.");
                }
            }
        }

        return result;
    }

    private async Task<bool> CustomerInTenantAsync(Guid? customerId, CancellationToken cancellationToken) =>
        customerId is not { } id
        || await _dbContext.Customers.AnyAsync(
            c => c.Id == id && c.TenantId == _tenantContext.TenantId, cancellationToken);

    /// <summary>
    /// The linked contact person must exist in this tenant AND belong to the same customer the
    /// location is linked to; anything else (foreign tenant, other customer, no customer link)
    /// is one and the same Dutch validation error — existence is never leaked.
    /// </summary>
    private async Task EnsureCustomerContactValidAsync(
        Guid? customerContactId, Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerContactId is not { } contactId)
        {
            return;
        }

        var contactCustomerId = await _dbContext.Set<CustomerContact>()
            .Where(c => c.Id == contactId && c.TenantId == _tenantContext.TenantId)
            .Select(c => (Guid?)c.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (contactCustomerId is null || customerId is null || contactCustomerId != customerId)
        {
            throw new DomainValidationException("customerContactId",
                "De gekozen contactpersoon hoort niet bij de gekoppelde klant.");
        }
    }

    /// <summary>Compact audit-friendly summary, e.g. "Ma 07:00–12:00, 13:00–17:00; Di 07:00–17:00".
    /// Extracted to <see cref="OpeningHoursFormatter"/> so the order-stop snapshot shares it.</summary>
    private static string? OpeningHoursSummary(IReadOnlyCollection<LocationOpeningInterval> intervals) =>
        OpeningHoursFormatter.Summarize(intervals);

    private static string? FormatTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Purpose-built readable audit payload (never a raw entity). The sensitive access code is
    /// masked: "•••" when set, null when empty — the raw value never reaches the audit log. The
    /// linked contact person is resolved to a name at write time (house history pattern).
    /// </summary>
    private async Task<object> BuildAuditPayloadAsync(Location l, CancellationToken cancellationToken)
    {
        string? contactPersonName = l.CustomerContactId is { } contactId
            ? await _dbContext.Set<CustomerContact>().AsNoTracking()
                .Where(c => c.Id == contactId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.DisplayName ?? c.FirstName + " " + c.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new
        {
            l.Code,
            l.Name,
            l.Type,
            l.IsActive,
            l.ExternalReference,
            l.ContactMobile,
            CustomerContact = contactPersonName,
            l.Gate,
            AccessCode = string.IsNullOrEmpty(l.AccessCode) ? null : "•••",
            l.ReceptionPoint,
            l.Dock,
            l.RouteDescription,
            l.DeliveryByAppointmentOnly,
            l.HeightRestrictionMeters,
            l.WeightRestrictionTons,
            l.AdrAllowed,
            l.CraneRequired,
            l.ForkliftAvailable,
            l.DriverInstructions,
            l.InternalMemo,
            l.DefaultLoadingMinutes,
            l.DefaultUnloadingMinutes,
            PreferredArrivalFrom = FormatTime(l.PreferredArrivalFrom),
            PreferredArrivalTo = FormatTime(l.PreferredArrivalTo),
            EarliestArrival = FormatTime(l.EarliestArrival),
            LatestArrival = FormatTime(l.LatestArrival),
            OpeningIntervals = OpeningHoursSummary(l.OpeningIntervals),
        };
    }

    private async Task<LocationDetailDto> MapToDetailAsync(Location l, CancellationToken cancellationToken, bool canViewSensitive = false)
    {
        string? customerName = l.CustomerId is { } cid
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == cid && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var intervals = await _dbContext.Set<LocationOpeningInterval>().AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.LocationId == l.Id)
            .OrderBy(i => i.DayOfWeek).ThenBy(i => i.FromTime)
            .ToListAsync(cancellationToken);

        return new LocationDetailDto(
            l.Id, l.Code, l.Name, l.Type,
            l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode,
            l.Latitude, l.Longitude,
            l.ContactName, l.ContactPhone, l.ContactEmail,
            l.OpeningHours, l.LoadingInstructions, l.UnloadingInstructions, l.AccessInstructions,
            l.AccessRestrictions, l.VehicleRestrictions, l.TrailerRestrictions,
            l.AlfapassRequired, l.AppointmentRequired, l.IsActive, l.CustomerId, customerName, l.Notes,
            l.IsDefaultLoadingLocation, l.IsDefaultUnloadingLocation, l.IsDefaultBillingLocation,
            ExternalReference: l.ExternalReference,
            ContactMobile: l.ContactMobile,
            CustomerContactId: l.CustomerContactId,
            Gate: l.Gate,
            AccessCode: canViewSensitive ? l.AccessCode : null,
            ReceptionPoint: l.ReceptionPoint,
            Dock: l.Dock,
            RouteDescription: l.RouteDescription,
            DeliveryByAppointmentOnly: l.DeliveryByAppointmentOnly,
            HeightRestrictionMeters: l.HeightRestrictionMeters,
            WeightRestrictionTons: l.WeightRestrictionTons,
            AdrAllowed: l.AdrAllowed,
            CraneRequired: l.CraneRequired,
            ForkliftAvailable: l.ForkliftAvailable,
            DriverInstructions: l.DriverInstructions,
            InternalMemo: l.InternalMemo,
            DefaultLoadingMinutes: l.DefaultLoadingMinutes,
            DefaultUnloadingMinutes: l.DefaultUnloadingMinutes,
            PreferredArrivalFrom: FormatTime(l.PreferredArrivalFrom),
            PreferredArrivalTo: FormatTime(l.PreferredArrivalTo),
            EarliestArrival: FormatTime(l.EarliestArrival),
            LatestArrival: FormatTime(l.LatestArrival),
            OpeningIntervals: intervals
                .Select(i => new LocationOpeningIntervalDto(i.DayOfWeek, FormatTime(i.FromTime)!, FormatTime(i.ToTime)!, i.Note))
                .ToList());
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
