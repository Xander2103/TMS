using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record InventoryOverviewRowDto(
    Guid TemplateId, Guid? VariantId, string Name, string? VariantLabel, string Category,
    string? StorageLocation, string? Unit, int CurrentStock,
    int? WarningLevel, int? MinimumLevel, int? TargetLevel, int? ReorderQuantity,
    InventoryStatus Status, DateTime? LastMovementAt, bool AllowNegativeStock, bool ReturnRequired);

public record InventoryAlertDto(
    Guid Id, Guid TemplateId, Guid? VariantId, string Name, string? VariantLabel,
    InventoryStatus Kind, int StockSnapshot, int? WarningSnapshot, int? MinimumSnapshot,
    DateTime LastSeenAt, DateTime CreatedAt);

public record InventoryLoanDto(
    Guid ItemId, Guid EmployeeId, string EmployeeName, string ItemName, string? VariantLabel,
    int Quantity, DateOnly? IssuedDate, DateOnly? ExpectedReturnDate, bool IsOverdue,
    string? ConditionAtIssue, string? SerialNumber, bool EmployeeActive);

public interface IInventoryInsightsService
{
    /// <summary>All stock-tracked targets (template or variant granularity) with live status.</summary>
    Task<IReadOnlyList<InventoryOverviewRowDto>> GetOverviewAsync(InventoryStatus? status, CancellationToken cancellationToken);

    /// <summary>Open (active) inventory alerts, worst first.</summary>
    Task<IReadOnlyList<InventoryAlertDto>> GetAlertsAsync(CancellationToken cancellationToken);

    /// <summary>Outstanding issued items with a return duty; overdue first.</summary>
    Task<IReadOnlyList<InventoryLoanDto>> GetLoansAsync(bool overdueOnly, CancellationToken cancellationToken);
}

public class InventoryInsightsService : IInventoryInsightsService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InventoryInsightsService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<InventoryOverviewRowDto>> GetOverviewAsync(
        InventoryStatus? status, CancellationToken cancellationToken)
    {
        var templates = await _dbContext.IssuedItemTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.IsActive && t.StockTrackingEnabled)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        var templateIds = templates.Select(t => t.Id).ToList();
        var variants = await _dbContext.IssuedItemVariants.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && templateIds.Contains(v.TemplateId) && v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Label)
            .ToListAsync(cancellationToken);
        var variantsByTemplate = variants.ToLookup(v => v.TemplateId);

        var lastMovements = await _dbContext.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId && templateIds.Contains(m.TemplateId))
            .GroupBy(m => new { m.TemplateId, m.VariantId })
            .Select(g => new { g.Key.TemplateId, g.Key.VariantId, Last = g.Max(m => m.Timestamp) })
            .ToListAsync(cancellationToken);
        var lastByTarget = lastMovements.ToDictionary(m => (m.TemplateId, m.VariantId), m => m.Last);

        var rows = new List<InventoryOverviewRowDto>();
        foreach (var template in templates)
        {
            if (template.VariantsEnabled)
            {
                foreach (var variant in variantsByTemplate[template.Id])
                {
                    var warning = variant.LowStockThreshold ?? template.LowStockThreshold;
                    rows.Add(new InventoryOverviewRowDto(
                        template.Id, variant.Id, template.Name, variant.Label, template.Category,
                        template.StorageLocation, template.Unit, variant.CurrentStock,
                        warning, template.MinimumStock, template.TargetStockLevel, template.ReorderQuantity,
                        InventoryStatusCalculator.Compute(variant.CurrentStock, warning, template.MinimumStock),
                        lastByTarget.TryGetValue((template.Id, (Guid?)variant.Id), out var lastVariant) ? lastVariant : null,
                        template.AllowNegativeStock, template.ReturnRequired));
                }
            }
            else
            {
                rows.Add(new InventoryOverviewRowDto(
                    template.Id, null, template.Name, null, template.Category,
                    template.StorageLocation, template.Unit, template.CurrentStock,
                    template.LowStockThreshold, template.MinimumStock, template.TargetStockLevel, template.ReorderQuantity,
                    InventoryStatusCalculator.Compute(template.CurrentStock, template.LowStockThreshold, template.MinimumStock),
                    lastByTarget.TryGetValue((template.Id, (Guid?)null), out var last) ? last : null,
                    template.AllowNegativeStock, template.ReturnRequired));
            }
        }

        return status is { } wanted ? rows.Where(r => r.Status == wanted).ToList() : rows;
    }

    public async Task<IReadOnlyList<InventoryLoanDto>> GetLoansAsync(bool overdueOnly, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var templateReturnRequired = _dbContext.IssuedItemTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ReturnRequired)
            .Select(t => t.Id);
        var rows = await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == IssuedItemStatus.Issued
                        && (i.ExpectedReturnDate != null
                            || (i.TemplateId != null && templateReturnRequired.Contains(i.TemplateId.Value))))
            .Join(_dbContext.Employees.AsNoTracking(), i => i.EmployeeId, e => e.Id,
                (i, e) => new
                {
                    i.Id, i.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName, e.IsActive,
                    i.NameSnapshot, i.VariantSnapshot, i.Quantity, i.IssuedDate, i.ExpectedReturnDate,
                    i.ConditionAtIssue, i.SerialNumber,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new InventoryLoanDto(
                r.Id, r.EmployeeId, r.EmployeeName, r.NameSnapshot, r.VariantSnapshot,
                r.Quantity, r.IssuedDate, r.ExpectedReturnDate,
                IsOverdue: r.ExpectedReturnDate is { } due && due < today,
                r.ConditionAtIssue, r.SerialNumber, r.IsActive))
            .Where(r => !overdueOnly || r.IsOverdue)
            .OrderByDescending(r => r.IsOverdue).ThenBy(r => r.ExpectedReturnDate).ThenBy(r => r.EmployeeName)
            .ToList();
    }

    public async Task<IReadOnlyList<InventoryAlertDto>> GetAlertsAsync(CancellationToken cancellationToken)
    {
        var alerts = await _dbContext.InventoryAlerts.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.Status == InventoryAlertStatus.Active)
            .ToListAsync(cancellationToken);
        var templateIds = alerts.Select(a => a.TemplateId).Distinct().ToList();
        var names = await _dbContext.IssuedItemTemplates.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.TenantId == _tenantContext.TenantId && templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var variantIds = alerts.Where(a => a.VariantId.HasValue).Select(a => a.VariantId!.Value).Distinct().ToList();
        var variantLabels = await _dbContext.IssuedItemVariants.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.TenantId == _tenantContext.TenantId && variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Label, cancellationToken);

        return alerts
            .OrderByDescending(a => a.Kind).ThenBy(a => a.LastSeenAt)
            .Select(a => new InventoryAlertDto(
                a.Id, a.TemplateId, a.VariantId,
                names.GetValueOrDefault(a.TemplateId, "(verwijderd artikel)"),
                a.VariantId is { } variantId ? variantLabels.GetValueOrDefault(variantId) : null,
                a.Kind, a.StockSnapshot, a.WarningSnapshot, a.MinimumSnapshot, a.LastSeenAt, a.CreatedAt))
            .ToList();
    }
}
