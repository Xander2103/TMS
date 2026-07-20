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
}
