using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Reference.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Reference.Controllers;

public record UnitTypeSettingsDto(Guid Id, string Code, string Name, bool IsActive, int SortOrder, bool AllowForOrderEntry, bool AllowForPricing);

public record SaveUnitTypeSettingsRequest(bool AllowForOrderEntry, bool AllowForPricing);

[Route("api/unit-types")]
public class UnitTypesController : LookupControllerBase<UnitType>
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitTypeMasterService _masterService;

    public UnitTypesController(ILookupService<UnitType> service, ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization, TransportationDbContext dbContext, ITenantContext tenantContext,
        IUnitTypeMasterService masterService)
        : base(service, currentUser, authorization)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _masterService = masterService;
    }

    protected override string ViewPermission => PermissionCodes.UnitTypesView;
    protected override string ManagePermission => PermissionCodes.UnitTypesManage;

    /// <summary>All unit types incl. the pricing/order-entry flags (settings page).</summary>
    [HttpGet("settings")]
    [RequirePermission(PermissionCodes.UnitTypesView, PermissionCodes.UnitTypesManage, PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<UnitTypeSettingsDto>>> Settings(CancellationToken cancellationToken)
    {
        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new UnitTypeSettingsDto(u.Id, u.Code, u.Name, u.IsActive, u.SortOrder, u.AllowForOrderEntry, u.AllowForPricing))
            .ToListAsync(cancellationToken);
        return Ok(units);
    }

    [HttpPut("{id:guid}/settings")]
    [RequirePermission(PermissionCodes.UnitTypesManage, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<UnitTypeSettingsDto>> SaveSettings(Guid id, SaveUnitTypeSettingsRequest request, CancellationToken cancellationToken)
    {
        var unit = await _dbContext.UnitTypes
            .FirstOrDefaultAsync(u => u.TenantId == _tenantContext.TenantId && u.Id == id, cancellationToken);
        if (unit is null)
        {
            return NotFound();
        }

        unit.AllowForOrderEntry = request.AllowForOrderEntry;
        unit.AllowForPricing = request.AllowForPricing;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new UnitTypeSettingsDto(unit.Id, unit.Code, unit.Name, unit.IsActive, unit.SortOrder, unit.AllowForOrderEntry, unit.AllowForPricing));
    }

    /// <summary>
    /// Full unit master list (Stamgegevens → Eenheden). Order-entry permissions may read it
    /// too: the order form needs the physical defaults for dimension autofill.
    /// </summary>
    [HttpGet("master")]
    [RequirePermission(PermissionCodes.UnitTypesView, PermissionCodes.UnitTypesManage, PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
        PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<UnitTypeMasterDto>>> Master(CancellationToken cancellationToken) =>
        Ok(await _masterService.ListAsync(cancellationToken));

    [HttpPost("master")]
    [RequirePermission(PermissionCodes.UnitTypesManage, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<UnitTypeMasterDto>> CreateMaster(SaveUnitTypeMasterRequest request, CancellationToken cancellationToken) =>
        Ok(await _masterService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}/master")]
    [RequirePermission(PermissionCodes.UnitTypesManage, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<UnitTypeMasterDto>> UpdateMaster(Guid id, SaveUnitTypeMasterRequest request, CancellationToken cancellationToken)
    {
        var updated = await _masterService.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Active stock units for the inventory-template "Voorraadeenheid" dropdown.</summary>
    [HttpGet("inventory-options")]
    [RequirePermission(PermissionCodes.IssuedItemsManageTemplates, PermissionCodes.InventoryView, PermissionCodes.InventoryManage,
        PermissionCodes.UnitTypesView, PermissionCodes.UnitTypesManage)]
    public async Task<ActionResult<IReadOnlyList<InventoryUnitOptionDto>>> InventoryOptions(CancellationToken cancellationToken)
    {
        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.IsActive && u.AllowForInventory)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new InventoryUnitOptionDto(u.Id, u.Code, u.Name, u.Symbol))
            .ToListAsync(cancellationToken);
        return Ok(units);
    }
}

public record InventoryUnitOptionDto(Guid Id, string Code, string Name, string? Symbol);
