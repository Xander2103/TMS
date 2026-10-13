using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Services;

public interface IWarehouseAdminService
{
    Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken cancellationToken);
    Task<WarehouseDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<WarehouseDto> CreateAsync(SaveWarehouseRequest request, CancellationToken cancellationToken);
    Task<WarehouseDto?> UpdateAsync(Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken);
    Task<WarehouseDto?> SaveDockAsync(Guid warehouseId, Guid? dockId, SaveDockRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteDockAsync(Guid warehouseId, Guid dockId, CancellationToken cancellationToken);

    // Wave 4 §1: storage locations (zone → position).
    Task<IReadOnlyList<WarehouseLocationDto>?> ListLocationsAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<WarehouseLocationDto?> SaveLocationAsync(Guid warehouseId, Guid? locationId, SaveWarehouseLocationRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteLocationAsync(Guid warehouseId, Guid locationId, CancellationToken cancellationToken);
}

/// <summary>Warehouse/dock master data. Addresses stay on the linked Location — never copied.</summary>
public class WarehouseAdminService : IWarehouseAdminService
{
    private const string EntityType = "Warehouse";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public WarehouseAdminService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<Warehouse> TenantScoped() =>
        _dbContext.Warehouses.Where(w => w.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken cancellationToken)
    {
        var warehouses = await TenantScoped().AsNoTracking()
            .Include(w => w.Docks)
            .OrderBy(w => w.Name)
            .Take(200)
            .ToListAsync(cancellationToken);
        var labels = await LocationLabelsAsync(warehouses.Select(w => w.LocationId), cancellationToken);
        return warehouses.Select(w => Map(w, labels)).ToList();
    }

    public async Task<WarehouseDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await TenantScoped().AsNoTracking()
            .Include(w => w.Docks)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        var labels = await LocationLabelsAsync([warehouse.LocationId], cancellationToken);
        return Map(warehouse, labels);
    }

    public async Task<WarehouseDto> CreateAsync(SaveWarehouseRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, excludeId: null, cancellationToken);

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
        };
        Apply(warehouse, request);
        _dbContext.Add(warehouse);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, warehouse.Id.ToString(), "Created", null,
            new { warehouse.Name, warehouse.LocationId }, cancellationToken);

        return (await GetAsync(warehouse.Id, cancellationToken))!;
    }

    public async Task<WarehouseDto?> UpdateAsync(Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await TenantScoped().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        await ValidateAsync(request, excludeId: id, cancellationToken);
        var before = new { warehouse.Name, warehouse.IsActive, warehouse.OpensAt, warehouse.ClosesAt };
        Apply(warehouse, request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, warehouse.Id.ToString(), "Updated", before,
            new { warehouse.Name, warehouse.IsActive, warehouse.OpensAt, warehouse.ClosesAt }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<WarehouseDto?> SaveDockAsync(
        Guid warehouseId, Guid? dockId, SaveDockRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await TenantScoped().Include(w => w.Docks)
            .FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new DomainValidationException("code", "De dockcode is verplicht.");
        }

        if (!request.AllowsLoading && !request.AllowsUnloading)
        {
            throw new DomainValidationException("allowsLoading", "Een dock moet laden en/of lossen toelaten.");
        }

        var code = request.Code.Trim();
        if (warehouse.Docks.Any(d => d.Id != dockId && string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException("code", "Deze dockcode bestaat al binnen het magazijn.");
        }

        Dock dock;
        if (dockId is { } existingId)
        {
            dock = warehouse.Docks.FirstOrDefault(d => d.Id == existingId)
                ?? throw new DomainValidationException("Het dock bestaat niet.");
        }
        else
        {
            dock = new Dock { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, WarehouseId = warehouseId };
            _dbContext.Add(dock);
        }

        dock.Code = code;
        dock.Name = Trim(request.Name);
        dock.AllowsLoading = request.AllowsLoading;
        dock.AllowsUnloading = request.AllowsUnloading;
        dock.AllowsAdr = request.AllowsAdr;
        dock.Refrigerated = request.Refrigerated;
        dock.MaxVehicleLengthM = request.MaxVehicleLengthM;
        dock.MaxVehicleHeightM = request.MaxVehicleHeightM;
        dock.IsActive = request.IsActive;
        dock.Notes = Trim(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, warehouseId.ToString(),
            dockId is null ? "DockCreated" : "DockUpdated", null,
            new { dock.Id, dock.Code, dock.IsActive }, cancellationToken);

        return await GetAsync(warehouseId, cancellationToken);
    }

    public async Task<bool> DeleteDockAsync(Guid warehouseId, Guid dockId, CancellationToken cancellationToken)
    {
        var dock = await _dbContext.Docks.FirstOrDefaultAsync(
            d => d.Id == dockId && d.WarehouseId == warehouseId && d.TenantId == _tenantContext.TenantId,
            cancellationToken);
        if (dock is null)
        {
            return false;
        }

        _dbContext.Remove(dock); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, warehouseId.ToString(), "DockDeleted",
            new { dock.Id, dock.Code }, null, cancellationToken);
        return true;
    }

    private async Task ValidateAsync(SaveWarehouseRequest request, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (!await _dbContext.Locations.AnyAsync(
                l => l.Id == request.LocationId && l.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            throw new DomainValidationException("locationId", "De gekoppelde locatie bestaat niet.");
        }

        if (request.OpensAt is { } opens && request.ClosesAt is { } closes && closes <= opens)
        {
            throw new DomainValidationException("closesAt", "Het sluitingsuur moet na het openingsuur liggen.");
        }

        var name = request.Name.Trim();
        if (await TenantScoped().AnyAsync(
                w => w.Id != excludeId && w.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            throw new DomainValidationException("name", "Er bestaat al een magazijn met deze naam.");
        }
    }

    private static void Apply(Warehouse warehouse, SaveWarehouseRequest request)
    {
        warehouse.Name = request.Name.Trim();
        warehouse.LocationId = request.LocationId;
        warehouse.IsActive = request.IsActive;
        warehouse.OpensAt = request.OpensAt;
        warehouse.ClosesAt = request.ClosesAt;
        warehouse.ContactName = Trim(request.ContactName);
        warehouse.ContactPhone = Trim(request.ContactPhone);
        warehouse.ContactEmail = Trim(request.ContactEmail);
        warehouse.Notes = Trim(request.Notes);
    }

    private async Task<Dictionary<Guid, string>> LocationLabelsAsync(
        IEnumerable<Guid> locationIds, CancellationToken cancellationToken)
    {
        var ids = locationIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && ids.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => $"{l.Name} — {l.City}", cancellationToken);
    }

    private static WarehouseDto Map(Warehouse w, IReadOnlyDictionary<Guid, string> labels) => new(
        w.Id, w.Name, w.LocationId, labels.GetValueOrDefault(w.LocationId, "?"), w.IsActive,
        w.OpensAt, w.ClosesAt, w.ContactName, w.ContactPhone, w.ContactEmail, w.Notes,
        w.Docks.Where(d => !d.IsDeleted).OrderBy(d => d.Code)
            .Select(d => new DockDto(d.Id, d.Code, d.Name, d.AllowsLoading, d.AllowsUnloading,
                d.AllowsAdr, d.Refrigerated, d.MaxVehicleLengthM, d.MaxVehicleHeightM, d.IsActive, d.Notes))
            .ToList());

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // --- Wave 4 §1: storage locations (zone → position) ---------------------------------------

    public async Task<IReadOnlyList<WarehouseLocationDto>?> ListLocationsAsync(
        Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await TenantScoped().AnyAsync(w => w.Id == warehouseId, cancellationToken))
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;
        var locations = await _dbContext.WarehouseLocations.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.WarehouseId == warehouseId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code)
            .ToListAsync(cancellationToken);
        var locationIds = locations.Select(l => l.Id).ToList();
        var packageCounts = locationIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.CurrentWarehouseLocationId != null
                            && locationIds.Contains(p.CurrentWarehouseLocationId.Value))
                .GroupBy(p => p.CurrentWarehouseLocationId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return locations
            .Select(l => new WarehouseLocationDto(
                l.Id, l.WarehouseId, l.ParentId, l.Code, l.Name, l.Kind, l.IsActive, l.SortOrder,
                packageCounts.GetValueOrDefault(l.Id)))
            .ToList();
    }

    public async Task<WarehouseLocationDto?> SaveLocationAsync(
        Guid warehouseId, Guid? locationId, SaveWarehouseLocationRequest request, CancellationToken cancellationToken)
    {
        if (!await TenantScoped().AnyAsync(w => w.Id == warehouseId, cancellationToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("code", "Code en naam zijn verplicht.");
        }

        var kind = request.Kind?.Trim() is "Position" ? "Position" : "Zone";
        var tenantId = _tenantContext.TenantId;
        var code = request.Code.Trim().ToUpperInvariant();

        if (request.ParentId is { } parentId)
        {
            var parent = await _dbContext.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == parentId, cancellationToken);
            if (parent is null || parent.WarehouseId != warehouseId)
            {
                throw new DomainValidationException("parentId", "De bovenliggende zone bestaat niet in dit magazijn.");
            }

            if (parent.ParentId is not null)
            {
                throw new DomainValidationException("parentId",
                    "Maximaal twee niveaus: een positie kan niet onder een andere positie hangen.");
            }
        }

        var duplicate = await _dbContext.WarehouseLocations.AnyAsync(
            l => l.TenantId == tenantId && l.WarehouseId == warehouseId && l.Code == code && l.Id != locationId,
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een locatie met code '{code}' in dit magazijn.");
        }

        WarehouseLocation location;
        if (locationId is { } existingId)
        {
            location = await _dbContext.WarehouseLocations.FirstOrDefaultAsync(
                    l => l.TenantId == tenantId && l.WarehouseId == warehouseId && l.Id == existingId, cancellationToken)
                ?? throw new DomainValidationException("id", "De locatie bestaat niet.");
        }
        else
        {
            location = new WarehouseLocation { Id = Guid.NewGuid(), TenantId = tenantId, WarehouseId = warehouseId };
            _dbContext.WarehouseLocations.Add(location);
        }

        location.ParentId = request.ParentId;
        location.Code = code;
        location.Name = request.Name.Trim();
        location.Kind = request.ParentId is not null ? "Position" : kind;
        location.IsActive = request.IsActive;
        location.SortOrder = request.SortOrder;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("WarehouseLocation", location.Id.ToString(),
            locationId is null ? "Created" : "Updated", null,
            new { location.WarehouseId, location.Code, location.Name, location.Kind, location.IsActive }, cancellationToken);

        return new WarehouseLocationDto(
            location.Id, location.WarehouseId, location.ParentId, location.Code, location.Name,
            location.Kind, location.IsActive, location.SortOrder);
    }

    public async Task<bool> DeleteLocationAsync(Guid warehouseId, Guid locationId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var location = await _dbContext.WarehouseLocations.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.WarehouseId == warehouseId && l.Id == locationId, cancellationToken);
        if (location is null)
        {
            return false;
        }

        if (await _dbContext.WarehouseLocations.AnyAsync(
                l => l.TenantId == tenantId && l.ParentId == locationId, cancellationToken))
        {
            throw new DomainValidationException("id", "Deze zone bevat nog posities. Verwijder die eerst.");
        }

        if (await _dbContext.Packages.AnyAsync(
                p => p.TenantId == tenantId && p.CurrentWarehouseLocationId == locationId, cancellationToken))
        {
            throw new DomainValidationException("id",
                "Op deze locatie staan nog colli. Verplaats die eerst (scan Verplaatsen).");
        }

        _dbContext.Remove(location);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("WarehouseLocation", location.Id.ToString(), "Deleted",
            new { location.WarehouseId, location.Code, location.Name }, null, cancellationToken);
        return true;
    }
}
