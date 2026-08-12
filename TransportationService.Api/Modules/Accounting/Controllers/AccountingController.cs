using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Accounting.Controllers;

/// <summary>
/// Bedrijfsinstellingen → Boekhouding: tenant-scoped ledger accounts and the sales-category →
/// account mappings (corrections wave §7). Reads on accounting.view, writes on accounting.manage.
/// </summary>
[ApiController]
[Route("api/accounting")]
public class AccountingController : ControllerBase
{
    private readonly IAccountingService _service;
    private readonly IAccountingExportService _export;
    private readonly IAuditService _auditService;

    public AccountingController(IAccountingService service, IAccountingExportService export, IAuditService auditService)
    {
        _service = service;
        _export = export;
        _auditService = auditService;
    }

    [HttpGet("ledger-accounts")]
    [RequirePermission(PermissionCodes.AccountingView, PermissionCodes.AccountingManage)]
    public async Task<ActionResult<IReadOnlyList<LedgerAccountDto>>> ListLedgerAccounts(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await _service.ListLedgerAccountsAsync(includeInactive, cancellationToken));

    [HttpPost("ledger-accounts")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<ActionResult<LedgerAccountDto>> CreateLedgerAccount(
        SaveLedgerAccountRequest request, CancellationToken cancellationToken)
        => Ok(await _service.CreateLedgerAccountAsync(request, cancellationToken));

    [HttpPut("ledger-accounts/{id:guid}")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<ActionResult<LedgerAccountDto>> UpdateLedgerAccount(
        Guid id, SaveLedgerAccountRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateLedgerAccountAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("ledger-accounts/{id:guid}")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<IActionResult> DeleteLedgerAccount(Guid id, CancellationToken cancellationToken)
        => await _service.DeleteLedgerAccountAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("sales-categories")]
    [RequirePermission(PermissionCodes.AccountingView, PermissionCodes.AccountingManage, PermissionCodes.InvoicesView,
        PermissionCodes.InvoicesEdit, PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<SalesCategoryDto>>> ListSalesCategories(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await _service.ListSalesCategoriesAsync(includeInactive, cancellationToken));

    [HttpPost("sales-categories")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<ActionResult<SalesCategoryDto>> CreateSalesCategory(
        SaveSalesCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await _service.CreateSalesCategoryAsync(request, cancellationToken));

    [HttpPut("sales-categories/{id:guid}")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<ActionResult<SalesCategoryDto>> UpdateSalesCategory(
        Guid id, SaveSalesCategoryRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateSalesCategoryAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("health")]
    [RequirePermission(PermissionCodes.AccountingView, PermissionCodes.AccountingManage)]
    public async Task<ActionResult<AccountingHealthDto>> GetHealth(CancellationToken cancellationToken)
        => Ok(await _service.GetHealthAsync(cancellationToken));

    /// <summary>XLSX export of Sent/Paid invoice lines with their frozen ledger snapshots (§7.4).</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionCodes.AccountingView, PermissionCodes.AccountingManage)]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var bytes = await _export.ExportAsync(from, to, cancellationToken);
        await _auditService.RecordExportAsync("accounting", new { from, to }, cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"boekhoudexport-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }
}
