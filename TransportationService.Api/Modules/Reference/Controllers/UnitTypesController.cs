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

/// <summary>MD-H8/M13: label of a unit code; <c>IsDeleted</c> marks a legacy code kept only for history.</summary>
public record UnitTypeLabelDto(string Code, string Name, bool IsActive, bool AllowForOrderEntry, bool AllowForPricing, bool IsDeleted);

public record SaveUnitTypeSettingsRequest(bool AllowForOrderEntry, bool AllowForPricing);

[Route("api/unit-types")]
public class UnitTypesController : LookupControllerBase<UnitType>
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitTypeMasterService _masterService;

    public UnitTypesController(ILookupService<UnitType> service, ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization, TransportationDbContext dbContext, ITenantContext tenantContext,
        IUnitTypeMasterService masterService, TransportationService.Api.Modules.Auditing.Services.IAuditService? auditService = null)
        : base(service, currentUser, authorization)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _masterService = masterService;
        _auditService = auditService;
    }

    private readonly TransportationService.Api.Modules.Auditing.Services.IAuditService? _auditService;

    protected override string ViewPermission => PermissionCodes.UnitTypesView;
    protected override string ManagePermission => PermissionCodes.UnitTypesManage;

    /// <summary>
    /// Transport acceptance T-H1: everyone who enters or reads goods / sales lines must be able to
    /// pick a unit. Loading the catalogue is USE, not master-data administration — it never grants
    /// create/update/delete on unit types (those stay on <see cref="ManagePermission"/>).
    /// </summary>
    protected override IReadOnlyList<string> OptionsPermissions =>
    [
        PermissionCodes.UnitTypesView, PermissionCodes.UnitTypesManage,
        PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
        PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage,
        PermissionCodes.DossiersView, PermissionCodes.DossiersManage, PermissionCodes.DossiersPrice,
    ];

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

        var before = UnitTypeMasterService.AuditShape(unit);
        unit.AllowForOrderEntry = request.AllowForOrderEntry;
        unit.AllowForPricing = request.AllowForPricing;
        await _dbContext.SaveChangesAsync(cancellationToken);
        // MD-M10: which units may be used for orders / pricing is configuration worth auditing.
        if (_auditService is not null && TransportationService.Api.Common.Integrity.AuditDiff.Changes(before, UnitTypeMasterService.AuditShape(unit)) is { } diff)
        {
            await _auditService.RecordAsync("UnitType", unit.Id.ToString(), "Updated", diff.Old, diff.New, cancellationToken);
        }
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

    /// <summary>
    /// MD-H8/M13: the units a user may PICK on goods / order lines — active and allowed for order
    /// entry (the generic options list ignored <c>AllowForOrderEntry</c>).
    /// </summary>
    [HttpGet("order-options")]
    public async Task<ActionResult<IReadOnlyList<LookupOptionDto>>> OrderOptions(CancellationToken cancellationToken)
    {
        if (await DeniedOptionsAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.IsActive && u.AllowForOrderEntry)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new LookupOptionDto(u.Id, u.Code, u.Name, false))
            .ToListAsync(cancellationToken);
        return Ok(units);
    }

    /// <summary>
    /// MD-H8/M13: code → label of EVERY unit the tenant ever had — inactive and deleted ones
    /// included — so a historical order line never falls back to the raw code ("4 TMKIST").
    /// </summary>
    [HttpGet("labels")]
    public async Task<ActionResult<IReadOnlyList<UnitTypeLabelDto>>> Labels(CancellationToken cancellationToken)
    {
        if (await DeniedOptionsAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var units = await _dbContext.UnitTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .OrderBy(u => u.IsDeleted).ThenBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new UnitTypeLabelDto(u.Code, u.Name, u.IsActive && !u.IsDeleted, u.AllowForOrderEntry, u.AllowForPricing, u.IsDeleted))
            .ToListAsync(cancellationToken);
        // One label per code: a live unit wins over a deleted one with the same (legacy) code.
        return Ok(units.GroupBy(u => u.Code).Select(g => g.First()).ToList());
    }

    // The former "inventory-options" endpoint is gone on purpose: issued-item templates use the
    // fixed catalogue in Modules/Employees/IssuedItemUnits.cs, not UnitType master data.
}
