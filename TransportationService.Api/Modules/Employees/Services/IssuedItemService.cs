using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record IssuedItemTemplateDto(
    Guid Id, string Name, string Category, string? ApplicableJobFunctionCodes,
    int DefaultQuantity, bool RequiresSerialNumber, bool RequiresReceivedDate, bool ReturnRequired,
    bool IsActive, int SortOrder,
    string? Description, string? Unit, string? Notes,
    bool StockTrackingEnabled, bool VariantsEnabled, bool AllowNegativeStock,
    int? LowStockThreshold, int? MinimumStock, string? StorageLocation,
    int CurrentStock, int TotalAvailable, bool LowStock, int VariantCount,
    Guid? CategoryId = null,
    int? TargetStockLevel = null, int? ReorderQuantity = null,
    bool NegativeStockRequiresReason = true,
    InventoryStatus StockStatus = InventoryStatus.Normal);

public record SaveIssuedItemTemplateRequest(
    string Name, string Category, string? ApplicableJobFunctionCodes,
    int DefaultQuantity, bool RequiresSerialNumber, bool RequiresReceivedDate, bool ReturnRequired,
    bool IsActive, int SortOrder,
    string? Description = null, string? Unit = null, string? Notes = null,
    bool StockTrackingEnabled = false, bool VariantsEnabled = false, bool AllowNegativeStock = false,
    int? LowStockThreshold = null, int? MinimumStock = null, string? StorageLocation = null,
    Guid? CategoryId = null,
    int? Stock = null, string? StockCorrectionReason = null,
    Guid? ExpectedVersion = null, bool ConfirmNegativeStock = false);

public record EmployeeIssuedItemDto(
    Guid Id, Guid? TemplateId, string Name, string Category, IssuedItemStatus Status,
    DateOnly? IssuedDate, int Quantity, string? SerialNumber, string? Notes, Guid? IssuedByUserId,
    DateOnly? ReturnedDate, string? ReturnCondition, Guid? ReceivedBackByUserId,
    Guid? VariantId = null, string? VariantLabel = null,
    DateOnly? ExpectedReturnDate = null, string? ConditionAtIssue = null,
    string? ReturnDisposition = null, bool IsReturnOverdue = false);

public record SaveEmployeeIssuedItemRequest(
    Guid? TemplateId, string? Name, string? Category, IssuedItemStatus Status,
    DateOnly? IssuedDate, int Quantity, string? SerialNumber, string? Notes,
    DateOnly? ReturnedDate, string? ReturnCondition,
    Guid? VariantId = null,
    string? ReturnDisposition = null,
    bool? RestoreStock = null,
    bool OverrideInsufficientStock = false,
    string? OverrideReason = null,
    Guid? ExpectedVersion = null,
    bool ConfirmNegativeStock = false,
    DateOnly? ExpectedReturnDate = null,
    string? ConditionAtIssue = null);

public interface IIssuedItemService
{
    Task<IReadOnlyList<IssuedItemTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken, Guid? categoryId = null);
    Task<IssuedItemTemplateDto> CreateTemplateAsync(SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken);
    Task<IssuedItemTemplateDto?> UpdateTemplateAsync(Guid id, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeIssuedItemDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeIssuedItemDto?> UpsertAsync(Guid employeeId, Guid? itemId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteItemAsync(Guid employeeId, Guid itemId, CancellationToken cancellationToken);
    Task<byte[]?> BuildAcknowledgementAsync(Guid employeeId, CancellationToken cancellationToken);
}

public class IssuedItemService : IIssuedItemService
{
    private const string TemplateEntity = "IssuedItemTemplate";
    private const string ItemEntity = "EmployeeIssuedItem";

    /// <summary>Structured return dispositions; only "good" may put stock back into circulation.</summary>
    private static readonly string[] ReturnDispositions = ["good", "damaged", "lost", "disposed"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;
    private readonly IInventoryService _inventoryService;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly INegativeStockGuard _negativeStockGuard;
    private readonly IInventoryAlertService? _alertService;

    public IssuedItemService(TransportationDbContext dbContext, ITenantContext tenantContext,
        ICurrentUserContext currentUser, IAuditService auditService,
        IInventoryService inventoryService, IPermissionAuthorizationService permissionService,
        INegativeStockGuard negativeStockGuard, IInventoryAlertService? alertService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditService = auditService;
        _inventoryService = inventoryService;
        _permissionService = permissionService;
        _negativeStockGuard = negativeStockGuard;
        _alertService = alertService;
    }

    public async Task<IReadOnlyList<IssuedItemTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken, Guid? categoryId = null)
    {
        var templates = await _dbContext.IssuedItemTemplates
            .Where(t => t.TenantId == _tenantContext.TenantId && (includeInactive || t.IsActive))
            .Where(t => categoryId == null || t.CategoryId == categoryId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var variantAggregates = await _dbContext.IssuedItemVariants.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId)
            .GroupBy(v => v.TemplateId)
            .Select(g => new { TemplateId = g.Key, Count = g.Count(), ActiveStock = g.Where(v => v.IsActive).Sum(v => v.CurrentStock) })
            .ToListAsync(cancellationToken);
        var aggregateByTemplate = variantAggregates.ToDictionary(a => a.TemplateId);

        return templates
            .Select(t =>
            {
                var aggregate = aggregateByTemplate.GetValueOrDefault(t.Id);
                return MapTemplate(t, aggregate?.ActiveStock ?? 0, aggregate?.Count ?? 0);
            })
            .ToList();
    }

    public async Task<IssuedItemTemplateDto> CreateTemplateAsync(SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = new IssuedItemTemplate { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(template, request);
        await ApplyCategoryAsync(template, request, cancellationToken);
        _dbContext.IssuedItemTemplates.Add(template);
        await ApplyStockTargetAsync(template, request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Created", null,
            new { template.Name, template.Category, template.StockTrackingEnabled, template.VariantsEnabled }, cancellationToken);
        return MapTemplate(template, 0, 0);
    }

    public async Task<IssuedItemTemplateDto?> UpdateTemplateAsync(Guid id, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await _dbContext.IssuedItemTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var oldStockTracking = template.StockTrackingEnabled;
        var oldVariants = template.VariantsEnabled;
        Apply(template, request);
        await ApplyCategoryAsync(template, request, cancellationToken);

        if (oldVariants && !template.VariantsEnabled)
        {
            var hasVariants = await _dbContext.IssuedItemVariants
                .AnyAsync(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == id, cancellationToken);
            if (hasVariants)
            {
                throw new DomainValidationException("variantsEnabled",
                    "Varianten kunnen niet uitgeschakeld worden zolang er varianten bestaan.");
            }
        }

        await ApplyStockTargetAsync(template, request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (_alertService is not null && !template.VariantsEnabled)
        {
            await _alertService.SyncAsync(template, null, cancellationToken);
        }

        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Updated", null,
            new { template.Name, template.IsActive }, cancellationToken);
        if (oldStockTracking != template.StockTrackingEnabled)
        {
            await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(),
                template.StockTrackingEnabled ? "StockTrackingEnabled" : "StockTrackingDisabled",
                new { Was = oldStockTracking }, new { template.StockTrackingEnabled }, cancellationToken);
        }

        var aggregate = await _dbContext.IssuedItemVariants.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == id)
            .GroupBy(v => v.TemplateId)
            .Select(g => new { Count = g.Count(), ActiveStock = g.Where(v => v.IsActive).Sum(v => v.CurrentStock) })
            .FirstOrDefaultAsync(cancellationToken);
        return MapTemplate(template, aggregate?.ActiveStock ?? 0, aggregate?.Count ?? 0);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _dbContext.IssuedItemTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return false;
        }

        _dbContext.Remove(template); // soft delete; historical employee items keep their snapshot
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Deleted", new { template.Name }, null, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EmployeeIssuedItemDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.EmployeeIssuedItems
            .Where(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId)
            .OrderBy(i => i.CategorySnapshot).ThenBy(i => i.NameSnapshot)
            .Select(i => Map(i))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeIssuedItemDto?> UpsertAsync(
        Guid employeeId, Guid? itemId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        ValidateDisposition(request.ReturnDisposition);

        EmployeeIssuedItem item;
        IssuedItemTemplate? template = null;
        var oldStatus = IssuedItemStatus.NotIssued;
        var oldQuantity = 0;

        if (itemId is { } id)
        {
            item = await _dbContext.EmployeeIssuedItems
                .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId && i.Id == id, cancellationToken)
                ?? throw new DomainValidationException("Het item bestaat niet.");
            oldStatus = item.Status;
            oldQuantity = item.Quantity;
            if (item.TemplateId is { } existingTemplateId)
            {
                template = await FindTemplateAsync(existingTemplateId, cancellationToken);
            }
        }
        else
        {
            item = new EmployeeIssuedItem { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, EmployeeId = employeeId };

            // Snapshot the name/category from the template (or the request) at issue time.
            if (request.TemplateId is { } templateId)
            {
                template = await FindTemplateAsync(templateId, cancellationToken)
                    ?? throw new DomainValidationException("templateId", "Het sjabloon bestaat niet.");
                item.TemplateId = templateId;
                item.NameSnapshot = template.Name;
                item.CategorySnapshot = template.Category;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    throw new DomainValidationException("name", "Een naam of sjabloon is verplicht.");
                }

                item.NameSnapshot = request.Name.Trim();
                item.CategorySnapshot = string.IsNullOrWhiteSpace(request.Category) ? "Algemeen" : request.Category.Trim();
            }

            _dbContext.EmployeeIssuedItems.Add(item);
        }

        var variant = await ResolveVariantAsync(item, template, request, oldStatus, cancellationToken);
        var stockBefore = template is { StockTrackingEnabled: true }
            ? variant?.CurrentStock ?? template.CurrentStock
            : (int?)null;
        ApplyItemState(item, request, oldStatus);
        await ApplyStockTransitionAsync(item, template, variant, oldStatus, oldQuantity, request, cancellationToken);

        // Item + movement + cached stock commit in ONE SaveChanges/transaction: a failed
        // employee-item write can never consume stock.
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_alertService is not null && template is not null && stockBefore is not null)
        {
            await _alertService.SyncAsync(template, variant, cancellationToken);
        }

        await _auditService.RecordAsync(ItemEntity, item.Id.ToString(), AuditAction(item.Status), null,
            new { item.EmployeeId, item.NameSnapshot, item.Status, item.VariantSnapshot, request.ReturnDisposition }, cancellationToken);
        if (stockBefore is { } previous && template is not null
            && (variant?.CurrentStock ?? template.CurrentStock) < 0 && previous >= 0)
        {
            await _auditService.RecordAsync(ItemEntity, item.Id.ToString(), "NegativeStockConfirmed",
                new { Previous = previous },
                new
                {
                    item.EmployeeId, item.NameSnapshot, item.VariantSnapshot,
                    ResultingStock = variant?.CurrentStock ?? template.CurrentStock,
                    Reason = request.OverrideReason,
                },
                cancellationToken);
        }

        return Map(item);
    }

    public async Task<bool> DeleteItemAsync(Guid employeeId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await _dbContext.EmployeeIssuedItems
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId && i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        // Deleting a still-issued, stock-tracked row gives the consumed quantity back so
        // the ledger never leaks stock into a removed record.
        if (item.Status == IssuedItemStatus.Issued && item.TemplateId is { } templateId)
        {
            var template = await FindTemplateAsync(templateId, cancellationToken);
            if (template is { StockTrackingEnabled: true })
            {
                var variant = item.VariantId is { } variantId ? await FindVariantAsync(templateId, variantId, cancellationToken) : null;
                if (!template.VariantsEnabled || variant is not null)
                {
                    _inventoryService.ApplyMovement(template, variant, StockMovementType.Return,
                        item.Quantity, null, "Registratie verwijderd", item.EmployeeId);
                }
            }
        }

        _dbContext.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(ItemEntity, item.Id.ToString(), "Deleted", new { item.NameSnapshot }, null, cancellationToken);
        return true;
    }

    public async Task<byte[]?> BuildAcknowledgementAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var items = await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId
                        && (i.Status == IssuedItemStatus.Issued || i.Status == IssuedItemStatus.Returned))
            .OrderBy(i => i.CategorySnapshot).ThenBy(i => i.NameSnapshot)
            .ToListAsync(cancellationToken);

        var companyName = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => s.TradingName ?? s.CompanyLegalName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        return IssuedItemAcknowledgementRenderer.Render(companyName, $"{employee.FirstName} {employee.LastName}",
            employee.EmployeeNumber, items);
    }

    // --- Stock integration ---

    /// <summary>
    /// Resolves and pins the variant for stock-tracked, variant-enabled templates. The
    /// variant of an already-issued item is immutable; on not-yet-issued rows it may still
    /// be switched.
    /// </summary>
    private async Task<IssuedItemVariant?> ResolveVariantAsync(
        EmployeeIssuedItem item, IssuedItemTemplate? template, SaveEmployeeIssuedItemRequest request,
        IssuedItemStatus oldStatus, CancellationToken cancellationToken)
    {
        if (template is null || !template.StockTrackingEnabled)
        {
            return null;
        }

        if (!template.VariantsEnabled)
        {
            if (request.VariantId is not null)
            {
                throw new DomainValidationException("variantId", "Dit sjabloon gebruikt geen varianten.");
            }

            return null;
        }

        var targetVariantId = request.VariantId ?? item.VariantId;
        if (targetVariantId is null)
        {
            throw new DomainValidationException("variantId", "Kies een variant (maat/uitvoering) voor dit sjabloon.");
        }

        if (item.VariantId is { } currentVariantId && currentVariantId != targetVariantId && oldStatus == IssuedItemStatus.Issued)
        {
            throw new DomainValidationException("variantId", "De variant van een uitgereikt item kan niet gewijzigd worden. Neem het item eerst in.");
        }

        var variant = await FindVariantAsync(template.Id, targetVariantId.Value, cancellationToken)
            ?? throw new DomainValidationException("variantId", "De variant bestaat niet.");
        item.VariantId = variant.Id;
        item.VariantSnapshot = variant.Label;
        return variant;
    }

    private async Task ApplyStockTransitionAsync(
        EmployeeIssuedItem item, IssuedItemTemplate? template, IssuedItemVariant? variant,
        IssuedItemStatus oldStatus, int oldQuantity, SaveEmployeeIssuedItemRequest request,
        CancellationToken cancellationToken)
    {
        // Stock disabled (or free-text item): behave exactly as before stock existed.
        if (template is null || !template.StockTrackingEnabled)
        {
            return;
        }

        if (template.VariantsEnabled && variant is null)
        {
            return; // variant no longer resolvable (historical row) — never guess stock
        }

        var newStatus = item.Status;
        var consumedBefore = oldStatus == IssuedItemStatus.Issued ? oldQuantity : 0;
        var consumedAfter = newStatus == IssuedItemStatus.Issued ? item.Quantity : 0;
        var delta = consumedAfter - consumedBefore; // > 0 → additional stock leaves

        if (delta > 0)
        {
            await EnsureAvailableAsync(template, variant, delta, request, cancellationToken);
            _inventoryService.ApplyMovement(template, variant, StockMovementType.Issue, -delta,
                Trim(request.OverrideReason), null, item.EmployeeId);
        }
        else if (delta < 0 && newStatus == IssuedItemStatus.Issued)
        {
            // Quantity lowered while issued: the surplus returns to stock.
            _inventoryService.ApplyMovement(template, variant, StockMovementType.Return, -delta,
                null, "Hoeveelheid verlaagd", item.EmployeeId);
        }

        if (consumedBefore > 0 && newStatus != IssuedItemStatus.Issued)
        {
            switch (newStatus)
            {
                case IssuedItemStatus.Returned:
                    var disposition = (request.ReturnDisposition ?? "good").ToLowerInvariant();
                    if (disposition == "good")
                    {
                        // Good return restores usable stock unless explicitly declined.
                        if (request.RestoreStock ?? true)
                        {
                            _inventoryService.ApplyMovement(template, variant, StockMovementType.Return,
                                consumedBefore, null, Trim(request.ReturnCondition), item.EmployeeId);
                        }
                    }
                    else
                    {
                        // Damaged/lost/disposed goods never re-enter usable stock; the
                        // zero-quantity line documents what happened to them.
                        _inventoryService.ApplyMovement(template, variant, DispositionMovement(disposition), 0,
                            null, Trim(request.ReturnCondition), item.EmployeeId);
                    }

                    break;
                case IssuedItemStatus.Missing:
                    _inventoryService.ApplyMovement(template, variant, StockMovementType.Lost, 0, null, Trim(request.Notes), item.EmployeeId);
                    break;
                case IssuedItemStatus.Damaged:
                    _inventoryService.ApplyMovement(template, variant, StockMovementType.Damaged, 0, null, Trim(request.Notes), item.EmployeeId);
                    break;
                case IssuedItemStatus.NotIssued:
                    // Issuance undone (registration error): the goods never left.
                    _inventoryService.ApplyMovement(template, variant, StockMovementType.Return,
                        consumedBefore, null, "Uitgifte ongedaan gemaakt", item.EmployeeId);
                    break;
            }
        }
    }

    /// <summary>
    /// Negative-stock policy for an issue of <paramref name="requested"/> units. The legacy
    /// OverrideInsufficientStock flag still counts as "confirmed" for API compatibility, but
    /// the guard additionally demands the current Version token — a bare boolean can no
    /// longer push stock below zero.
    /// </summary>
    private Task EnsureAvailableAsync(
        IssuedItemTemplate template, IssuedItemVariant? variant, int requested,
        SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken) =>
        _negativeStockGuard.EnsureAllowedAsync(template, variant, -requested,
            new NegativeStockConfirmation(
                request.ConfirmNegativeStock || request.OverrideInsufficientStock,
                request.ExpectedVersion, request.OverrideReason),
            cancellationToken);

    private static StockMovementType DispositionMovement(string disposition) => disposition switch
    {
        "damaged" => StockMovementType.Damaged,
        "lost" => StockMovementType.Lost,
        _ => StockMovementType.Disposed,
    };

    private static void ValidateDisposition(string? disposition)
    {
        if (disposition is not null && !ReturnDispositions.Contains(disposition.ToLowerInvariant()))
        {
            throw new DomainValidationException("returnDisposition", "Ongeldige retourconditie. Gebruik good, damaged, lost of disposed.");
        }
    }

    private static string AuditAction(IssuedItemStatus status) => status switch
    {
        IssuedItemStatus.Issued => "Issued",
        IssuedItemStatus.Returned => "Returned",
        IssuedItemStatus.Damaged => "MarkedDamaged",
        IssuedItemStatus.Missing => "MarkedMissing",
        _ => "Recorded",
    };

    private Task<IssuedItemTemplate?> FindTemplateAsync(Guid templateId, CancellationToken cancellationToken) =>
        _dbContext.IssuedItemTemplates.FirstOrDefaultAsync(
            t => t.TenantId == _tenantContext.TenantId && t.Id == templateId, cancellationToken);

    private Task<IssuedItemVariant?> FindVariantAsync(Guid templateId, Guid variantId, CancellationToken cancellationToken) =>
        _dbContext.IssuedItemVariants.FirstOrDefaultAsync(
            v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId && v.Id == variantId, cancellationToken);

    /// <summary>oldStatus drives the who-did-it stamps: the user who flips a row INTO Issued
    /// is the issuer; the one who flips it out of Issued received the goods back.</summary>
    private void ApplyItemState(EmployeeIssuedItem item, SaveEmployeeIssuedItemRequest request, IssuedItemStatus oldStatus)
    {
        item.Status = request.Status;
        item.IssuedDate = request.IssuedDate;
        item.Quantity = Math.Max(1, request.Quantity);
        item.SerialNumber = Trim(request.SerialNumber);
        item.Notes = Trim(request.Notes);
        item.ExpectedReturnDate = request.ExpectedReturnDate;
        item.ConditionAtIssue = Trim(request.ConditionAtIssue) ?? item.ConditionAtIssue;
        item.ReturnedDate = request.ReturnedDate;
        item.ReturnCondition = Trim(request.ReturnCondition);

        if (request.Status == IssuedItemStatus.Issued && oldStatus != IssuedItemStatus.Issued)
        {
            item.IssuedByUserId = _currentUser.CurrentUserId;
            item.ReturnDisposition = null;
            item.OverdueNotifiedAt = null;
        }
        else if (oldStatus == IssuedItemStatus.Issued && request.Status != IssuedItemStatus.Issued)
        {
            item.ReceivedBackByUserId = _currentUser.CurrentUserId;
            item.ReturnDisposition = request.Status == IssuedItemStatus.Returned
                ? (request.ReturnDisposition ?? "good").ToLowerInvariant()
                : item.ReturnDisposition;
        }
    }

    private void Apply(IssuedItemTemplate template, SaveIssuedItemTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (request.LowStockThreshold is < 0)
        {
            throw new DomainValidationException("lowStockThreshold", "De lage-voorraadgrens kan niet negatief zijn.");
        }

        if (request.VariantsEnabled && !request.StockTrackingEnabled)
        {
            throw new DomainValidationException("variantsEnabled", "Varianten vereisen dat voorraadbeheer aan staat.");
        }

        template.Name = request.Name.Trim();
        template.Category = string.IsNullOrWhiteSpace(request.Category) ? "Algemeen" : request.Category.Trim();
        template.ApplicableJobFunctionCodes = Trim(request.ApplicableJobFunctionCodes);
        template.DefaultQuantity = Math.Max(1, request.DefaultQuantity);
        template.RequiresSerialNumber = request.RequiresSerialNumber;
        template.RequiresReceivedDate = request.RequiresReceivedDate;
        template.ReturnRequired = request.ReturnRequired;
        template.IsActive = request.IsActive;
        template.SortOrder = request.SortOrder;
        template.Description = Trim(request.Description);
        template.Unit = Trim(request.Unit);
        template.Notes = Trim(request.Notes);
        template.StockTrackingEnabled = request.StockTrackingEnabled;
        template.VariantsEnabled = request.VariantsEnabled;
        template.AllowNegativeStock = request.AllowNegativeStock;
        template.LowStockThreshold = request.LowStockThreshold;
        template.MinimumStock = request.MinimumStock;
        template.StorageLocation = Trim(request.StorageLocation);
    }

    /// <summary>
    /// Applies the "Voorraad" value from the template form through the stock ledger. The first
    /// set becomes InitialStock; later changes append a Correction with a mandatory reason, so
    /// the auditable movement history is never overwritten. Variant-enabled templates hold
    /// stock on their variants and ignore the template-level value.
    /// </summary>
    private async Task ApplyStockTargetAsync(IssuedItemTemplate template, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        if (request.Stock is not { } target || !template.StockTrackingEnabled || template.VariantsEnabled)
        {
            return;
        }

        if (target == template.CurrentStock)
        {
            return;
        }

        await _negativeStockGuard.EnsureAllowedAsync(template, null, target - template.CurrentStock,
            new NegativeStockConfirmation(request.ConfirmNegativeStock, request.ExpectedVersion, request.StockCorrectionReason),
            cancellationToken);

        var hasMovements = await _dbContext.StockMovements.AnyAsync(
            m => m.TenantId == _tenantContext.TenantId && m.TemplateId == template.Id && m.VariantId == null,
            cancellationToken);
        if (!hasMovements)
        {
            _inventoryService.ApplyMovement(template, null, StockMovementType.InitialStock,
                target - template.CurrentStock, null, "Beginvoorraad via sjabloonformulier", null);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.StockCorrectionReason))
        {
            throw new DomainValidationException("stockCorrectionReason",
                "Een reden is verplicht bij het aanpassen van bestaande voorraad.");
        }

        _inventoryService.ApplyMovement(template, null, StockMovementType.Correction,
            target - template.CurrentStock, request.StockCorrectionReason.Trim(), null, null);
    }

    /// <summary>
    /// Resolves the master-data category and syncs the denormalized name snapshot. Without a
    /// CategoryId the legacy free-text Category from the request is kept as-is.
    /// </summary>
    private async Task ApplyCategoryAsync(IssuedItemTemplate template, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        if (request.CategoryId is { } categoryId)
        {
            var category = await _dbContext.IssuedItemCategories
                .FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == categoryId, cancellationToken)
                ?? throw new DomainValidationException("categoryId", "De categorie bestaat niet.");
            template.CategoryId = category.Id;
            template.Category = category.Name;
        }
        else
        {
            template.CategoryId = null;
        }
    }

    private Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        _dbContext.Employees.AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId, cancellationToken);

    /// <summary>Shared template mapping; variantStockSum/variantCount come from the variant aggregates.</summary>
    public static IssuedItemTemplateDto MapTemplate(IssuedItemTemplate t, int variantStockSum, int variantCount = 0)
    {
        var total = t.VariantsEnabled ? variantStockSum : t.CurrentStock;
        var status = t.StockTrackingEnabled
            ? InventoryStatusCalculator.Compute(total, t.LowStockThreshold, t.MinimumStock)
            : InventoryStatus.Normal;
        var low = t.StockTrackingEnabled && t.LowStockThreshold is { } threshold && total <= threshold;
        return new(
            t.Id, t.Name, t.Category, t.ApplicableJobFunctionCodes,
            t.DefaultQuantity, t.RequiresSerialNumber, t.RequiresReceivedDate, t.ReturnRequired, t.IsActive, t.SortOrder,
            t.Description, t.Unit, t.Notes,
            t.StockTrackingEnabled, t.VariantsEnabled, t.AllowNegativeStock,
            t.LowStockThreshold, t.MinimumStock, t.StorageLocation,
            t.CurrentStock, total, low, variantCount,
            t.CategoryId,
            t.TargetStockLevel, t.ReorderQuantity, t.NegativeStockRequiresReason, status);
    }

    private static EmployeeIssuedItemDto Map(EmployeeIssuedItem i) => new(
        i.Id, i.TemplateId, i.NameSnapshot, i.CategorySnapshot, i.Status,
        i.IssuedDate, i.Quantity, i.SerialNumber, i.Notes, i.IssuedByUserId,
        i.ReturnedDate, i.ReturnCondition, i.ReceivedBackByUserId,
        i.VariantId, i.VariantSnapshot,
        i.ExpectedReturnDate, i.ConditionAtIssue, i.ReturnDisposition,
        IsReturnOverdue: i.Status == IssuedItemStatus.Issued
            && i.ExpectedReturnDate is { } due && due < DateOnly.FromDateTime(DateTime.UtcNow));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
