using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record IssuedItemAttributeOptionDto(Guid Id, string Value, int SortOrder, bool IsActive);

public record IssuedItemAttributeDefinitionDto(
    Guid Id, string Name, bool AllowCustomValues, bool IsShared, int SortOrder, bool IsActive,
    IReadOnlyList<IssuedItemAttributeOptionDto> Options);

public record SaveAttributeDefinitionRequest(string Name, bool AllowCustomValues, bool IsShared, int SortOrder, bool IsActive);

public record SaveAttributeOptionRequest(string Value, int SortOrder, bool IsActive);

public record IssuedItemVariantValueDto(Guid AttributeDefinitionId, string AttributeName, Guid? AttributeOptionId, string Value);

public record IssuedItemVariantDto(
    Guid Id, string Label, int CurrentStock, bool IsActive, int SortOrder,
    IReadOnlyList<IssuedItemVariantValueDto> Values, int? LowStockThreshold = null);

public record SaveVariantValueRequest(Guid AttributeDefinitionId, Guid? AttributeOptionId, string? CustomValue);

/// <summary>
/// Label is used for label-only variants (templates without linked attributes, e.g. "Small",
/// "maat 43"); when attributes are linked the label is generated from Values instead.
/// </summary>
public record SaveVariantRequest(
    IReadOnlyList<SaveVariantValueRequest> Values, bool IsActive, int SortOrder, int? InitialStock,
    string? Label = null, int? LowStockThreshold = null);

public record GenerateVariantsDimension(Guid AttributeDefinitionId, IReadOnlyList<Guid> OptionIds);

public record GenerateVariantsRequest(IReadOnlyList<GenerateVariantsDimension> Dimensions);

public record StockMovementDto(
    Guid Id, Guid TemplateId, Guid? VariantId, string? VariantLabel, StockMovementType MovementType,
    int Quantity, int ResultingStock, string? Reason, string? Notes,
    Guid? EmployeeId, string? EmployeeName, Guid? PerformedByUserId, DateTime Timestamp);

public record StockReceiptRequest(Guid? VariantId, int Quantity, string? Notes);

public record StockCorrectionRequest(Guid? VariantId, int NewQuantity, string Reason);

public record CurrentHolderDto(
    Guid EmployeeId, string EmployeeName, string EmployeeNumber, Guid ItemId,
    string? VariantLabel, int Quantity, DateOnly? IssuedDate, string? SerialNumber);

public record IssuedItemTemplateDetailDto(
    IssuedItemTemplateDto Template,
    IReadOnlyList<IssuedItemAttributeDefinitionDto> Attributes,
    IReadOnlyList<IssuedItemVariantDto> Variants);

public interface IInventoryService
{
    // Attribute definitions + options (reusable master data).
    Task<IReadOnlyList<IssuedItemAttributeDefinitionDto>> ListAttributeDefinitionsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<IssuedItemAttributeDefinitionDto> CreateAttributeDefinitionAsync(SaveAttributeDefinitionRequest request, CancellationToken cancellationToken);
    Task<IssuedItemAttributeDefinitionDto?> UpdateAttributeDefinitionAsync(Guid id, SaveAttributeDefinitionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAttributeDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<IssuedItemAttributeOptionDto?> AddAttributeOptionAsync(Guid definitionId, SaveAttributeOptionRequest request, CancellationToken cancellationToken);
    Task<IssuedItemAttributeOptionDto?> UpdateAttributeOptionAsync(Guid definitionId, Guid optionId, SaveAttributeOptionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAttributeOptionAsync(Guid definitionId, Guid optionId, CancellationToken cancellationToken);

    // Template detail, template↔attribute selection and variants.
    Task<IssuedItemTemplateDetailDto?> GetTemplateDetailAsync(Guid templateId, CancellationToken cancellationToken);
    Task<IssuedItemTemplateDetailDto?> SetTemplateAttributesAsync(Guid templateId, IReadOnlyList<Guid> attributeDefinitionIds, CancellationToken cancellationToken);
    Task<IssuedItemVariantDto?> CreateVariantAsync(Guid templateId, SaveVariantRequest request, CancellationToken cancellationToken);
    Task<IssuedItemTemplateDetailDto?> GenerateVariantsAsync(Guid templateId, GenerateVariantsRequest request, CancellationToken cancellationToken);
    Task<IssuedItemVariantDto?> UpdateVariantAsync(Guid templateId, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteVariantAsync(Guid templateId, Guid variantId, CancellationToken cancellationToken);

    // Stock.
    Task<StockMovementDto?> ReceiveStockAsync(Guid templateId, StockReceiptRequest request, CancellationToken cancellationToken);
    Task<StockMovementDto?> CorrectStockAsync(Guid templateId, StockCorrectionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockMovementDto>?> ListMovementsAsync(Guid templateId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CurrentHolderDto>?> ListCurrentHoldersAsync(Guid templateId, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a movement to the tracked context and updates the owning cached stock;
    /// the caller owns SaveChanges so item + movement + cache commit in one transaction.
    /// </summary>
    StockMovement ApplyMovement(
        IssuedItemTemplate template, IssuedItemVariant? variant, StockMovementType type,
        int quantity, string? reason, string? notes, Guid? employeeId);
}

public class InventoryService : IInventoryService
{
    private const string DefinitionEntity = "IssuedItemAttributeDefinition";
    private const string VariantEntity = "IssuedItemVariant";
    private const string MovementEntity = "StockMovement";
    private const string TemplateEntity = "IssuedItemTemplate";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILowStockNotifier? _lowStockNotifier;

    public InventoryService(TransportationDbContext dbContext, ITenantContext tenantContext,
        ICurrentUserContext currentUser, IAuditService auditService, ILowStockNotifier? lowStockNotifier = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditService = auditService;
        _lowStockNotifier = lowStockNotifier;
    }

    // --- Attribute definitions ---

    public async Task<IReadOnlyList<IssuedItemAttributeDefinitionDto>> ListAttributeDefinitionsAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.IssuedItemAttributeDefinitions
            .Where(d => d.TenantId == _tenantContext.TenantId && (includeInactive || d.IsActive))
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);
        var options = await _dbContext.IssuedItemAttributeOptions
            .Where(o => o.TenantId == _tenantContext.TenantId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Value)
            .ToListAsync(cancellationToken);
        var byDefinition = options.ToLookup(o => o.AttributeDefinitionId);
        return definitions.Select(d => Map(d, byDefinition[d.Id])).ToList();
    }

    public async Task<IssuedItemAttributeDefinitionDto> CreateAttributeDefinitionAsync(
        SaveAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        var definition = new IssuedItemAttributeDefinition { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        ApplyDefinition(definition, request);
        _dbContext.IssuedItemAttributeDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(DefinitionEntity, definition.Id.ToString(), "Created",
            null, new { definition.Name, definition.IsShared }, cancellationToken);
        return Map(definition, []);
    }

    public async Task<IssuedItemAttributeDefinitionDto?> UpdateAttributeDefinitionAsync(
        Guid id, SaveAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(id, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        ApplyDefinition(definition, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(DefinitionEntity, definition.Id.ToString(), "Updated",
            null, new { definition.Name, definition.IsActive }, cancellationToken);
        var options = await ListOptionsAsync(id, cancellationToken);
        return new IssuedItemAttributeDefinitionDto(
            definition.Id, definition.Name, definition.AllowCustomValues, definition.IsShared,
            definition.SortOrder, definition.IsActive, options);
    }

    public async Task<bool> DeleteAttributeDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(id, cancellationToken);
        if (definition is null)
        {
            return false;
        }

        var inUse = await _dbContext.IssuedItemTemplateAttributes
            .AnyAsync(a => a.TenantId == _tenantContext.TenantId && a.AttributeDefinitionId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException("Dit attribuut wordt door één of meer sjablonen gebruikt en kan niet verwijderd worden. Zet het eventueel inactief.");
        }

        _dbContext.Remove(definition); // soft delete; variant values keep their name snapshot
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(DefinitionEntity, definition.Id.ToString(), "Deleted", new { definition.Name }, null, cancellationToken);
        return true;
    }

    public async Task<IssuedItemAttributeOptionDto?> AddAttributeOptionAsync(
        Guid definitionId, SaveAttributeOptionRequest request, CancellationToken cancellationToken)
    {
        var definition = await FindDefinitionAsync(definitionId, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        var value = RequireValue(request.Value);
        var duplicate = await _dbContext.IssuedItemAttributeOptions.AnyAsync(
            o => o.TenantId == _tenantContext.TenantId && o.AttributeDefinitionId == definitionId && o.Value == value,
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("value", "Deze waarde bestaat al voor dit attribuut.");
        }

        var option = new IssuedItemAttributeOption
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            AttributeDefinitionId = definitionId,
            Value = value,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };
        _dbContext.IssuedItemAttributeOptions.Add(option);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(DefinitionEntity, definitionId.ToString(), "OptionAdded",
            null, new { definition.Name, option.Value }, cancellationToken);
        return Map(option);
    }

    public async Task<IssuedItemAttributeOptionDto?> UpdateAttributeOptionAsync(
        Guid definitionId, Guid optionId, SaveAttributeOptionRequest request, CancellationToken cancellationToken)
    {
        var option = await _dbContext.IssuedItemAttributeOptions.FirstOrDefaultAsync(
            o => o.TenantId == _tenantContext.TenantId && o.AttributeDefinitionId == definitionId && o.Id == optionId,
            cancellationToken);
        if (option is null)
        {
            return null;
        }

        option.Value = RequireValue(request.Value);
        option.SortOrder = request.SortOrder;
        option.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(option);
    }

    public async Task<bool> DeleteAttributeOptionAsync(Guid definitionId, Guid optionId, CancellationToken cancellationToken)
    {
        var option = await _dbContext.IssuedItemAttributeOptions.FirstOrDefaultAsync(
            o => o.TenantId == _tenantContext.TenantId && o.AttributeDefinitionId == definitionId && o.Id == optionId,
            cancellationToken);
        if (option is null)
        {
            return false;
        }

        _dbContext.Remove(option); // soft delete; variant values carry the literal value
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // --- Template detail + attributes + variants ---

    public async Task<IssuedItemTemplateDetailDto?> GetTemplateDetailAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        return template is null ? null : await BuildDetailAsync(template, cancellationToken);
    }

    public async Task<IssuedItemTemplateDetailDto?> SetTemplateAttributesAsync(
        Guid templateId, IReadOnlyList<Guid> attributeDefinitionIds, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var distinct = attributeDefinitionIds.Distinct().ToList();
        var known = await _dbContext.IssuedItemAttributeDefinitions
            .Where(d => d.TenantId == _tenantContext.TenantId && distinct.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        if (known.Count != distinct.Count)
        {
            throw new DomainValidationException("attributeDefinitionIds", "Eén of meer attributen bestaan niet.");
        }

        var existing = await _dbContext.IssuedItemTemplateAttributes
            .Where(a => a.TenantId == _tenantContext.TenantId && a.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        // A variant already carries values for its attributes; removing an attribute that
        // variants still use would silently orphan those values.
        var removedIds = existing.Where(a => !distinct.Contains(a.AttributeDefinitionId)).Select(a => a.AttributeDefinitionId).ToList();
        if (removedIds.Count > 0)
        {
            var usedByVariant = await _dbContext.IssuedItemVariantValues
                .Where(v => v.TenantId == _tenantContext.TenantId && removedIds.Contains(v.AttributeDefinitionId))
                .Join(_dbContext.IssuedItemVariants.Where(v => v.TemplateId == templateId),
                    value => value.VariantId, variant => variant.Id, (value, variant) => value.Id)
                .AnyAsync(cancellationToken);
            if (usedByVariant)
            {
                throw new DomainValidationException("attributeDefinitionIds",
                    "Een attribuut kan niet losgekoppeld worden zolang varianten er waarden voor hebben.");
            }
        }

        _dbContext.IssuedItemTemplateAttributes.RemoveRange(existing.Where(a => !distinct.Contains(a.AttributeDefinitionId)));
        for (var index = 0; index < distinct.Count; index++)
        {
            var definitionId = distinct[index];
            var row = existing.FirstOrDefault(a => a.AttributeDefinitionId == definitionId);
            if (row is null)
            {
                _dbContext.IssuedItemTemplateAttributes.Add(new IssuedItemTemplateAttribute
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    TemplateId = templateId,
                    AttributeDefinitionId = definitionId,
                    SortOrder = index,
                });
            }
            else
            {
                row.SortOrder = index;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, templateId.ToString(), "AttributesChanged",
            null, new { template.Name, AttributeCount = distinct.Count }, cancellationToken);
        return await BuildDetailAsync(template, cancellationToken);
    }

    public async Task<IssuedItemVariantDto?> CreateVariantAsync(Guid templateId, SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        if (!template.VariantsEnabled)
        {
            throw new DomainValidationException("Dit sjabloon gebruikt geen varianten.");
        }

        var (label, values) = await ResolveLabelAndValuesAsync(templateId, request, cancellationToken);

        var duplicate = await _dbContext.IssuedItemVariants.AnyAsync(
            v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId && v.Label == label, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("values", $"De variant \"{label}\" bestaat al.");
        }

        var variant = new IssuedItemVariant
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TemplateId = templateId,
            Label = label,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            LowStockThreshold = NonNegativeOrNull(request.LowStockThreshold),
        };
        _dbContext.IssuedItemVariants.Add(variant);
        foreach (var value in values)
        {
            value.VariantId = variant.Id;
            _dbContext.IssuedItemVariantValues.Add(value);
        }

        if (request.InitialStock is { } initial and not 0)
        {
            if (initial < 0)
            {
                throw new DomainValidationException("initialStock", "Beginvoorraad kan niet negatief zijn.");
            }

            ApplyMovement(template, variant, StockMovementType.InitialStock, initial, null, "Beginvoorraad bij aanmaak variant", null);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(VariantEntity, variant.Id.ToString(), "Created",
            null, new { template.Name, variant.Label }, cancellationToken);
        return Map(variant, values.Select(MapValue).ToList());
    }

    /// <summary>
    /// Bulk-creates the cartesian product of the selected attribute options as variants
    /// (stock 0), skipping combinations that already exist so re-running with a broader
    /// selection only adds the missing ones. Also (re)pins the template's attribute set to
    /// the given dimensions, in order.
    /// </summary>
    public async Task<IssuedItemTemplateDetailDto?> GenerateVariantsAsync(
        Guid templateId, GenerateVariantsRequest request, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        if (!template.VariantsEnabled)
        {
            throw new DomainValidationException("Dit sjabloon gebruikt geen varianten.");
        }

        if (request.Dimensions.Count == 0 || request.Dimensions.Any(d => d.OptionIds.Count == 0))
        {
            throw new DomainValidationException("dimensions", "Kies per attribuut minstens één waarde.");
        }

        var comboCount = request.Dimensions.Aggregate(1, (acc, d) => acc * d.OptionIds.Count);
        if (comboCount > 500)
        {
            throw new DomainValidationException("dimensions", "Te veel combinaties (max. 500). Beperk de selectie.");
        }

        // Pin the template's attributes to the chosen dimensions (guards against orphaning
        // values of attributes still used by existing variants).
        await SetTemplateAttributesAsync(templateId, request.Dimensions.Select(d => d.AttributeDefinitionId).ToList(), cancellationToken);

        // Validate the selected options up front so a bad id fails the whole request.
        var optionsByDefinition = new Dictionary<Guid, List<IssuedItemAttributeOption>>();
        foreach (var dimension in request.Dimensions)
        {
            var optionIds = dimension.OptionIds.Distinct().ToList();
            var options = await _dbContext.IssuedItemAttributeOptions
                .Where(o => o.TenantId == _tenantContext.TenantId
                            && o.AttributeDefinitionId == dimension.AttributeDefinitionId
                            && optionIds.Contains(o.Id))
                .ToListAsync(cancellationToken);
            if (options.Count != optionIds.Count)
            {
                throw new DomainValidationException("dimensions", "Eén of meer gekozen waarden bestaan niet.");
            }

            optionsByDefinition[dimension.AttributeDefinitionId] = optionIds
                .Select(id => options.First(o => o.Id == id))
                .ToList();
        }

        var existingLabels = (await _dbContext.IssuedItemVariants
                .Where(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId)
                .Select(v => v.Label)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortOrder = existingLabels.Count;

        // Cartesian product in dimension order; each combo becomes one concrete variant.
        var combos = new List<List<IssuedItemAttributeOption>> { new() };
        foreach (var dimension in request.Dimensions)
        {
            combos = combos
                .SelectMany(prefix => optionsByDefinition[dimension.AttributeDefinitionId]
                    .Select(option => new List<IssuedItemAttributeOption>(prefix) { option }))
                .ToList();
        }

        var definitionNames = await _dbContext.IssuedItemAttributeDefinitions
            .Where(d => d.TenantId == _tenantContext.TenantId
                        && request.Dimensions.Select(x => x.AttributeDefinitionId).Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var created = 0;
        foreach (var combo in combos)
        {
            var label = string.Join(" / ", combo.Select(o => o.Value));
            if (!existingLabels.Add(label))
            {
                continue;
            }

            var variant = new IssuedItemVariant
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TemplateId = templateId,
                Label = label,
                IsActive = true,
                SortOrder = sortOrder++,
            };
            _dbContext.IssuedItemVariants.Add(variant);
            foreach (var option in combo)
            {
                _dbContext.IssuedItemVariantValues.Add(new IssuedItemVariantValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    VariantId = variant.Id,
                    AttributeDefinitionId = option.AttributeDefinitionId,
                    AttributeNameSnapshot = definitionNames.GetValueOrDefault(option.AttributeDefinitionId, ""),
                    AttributeOptionId = option.Id,
                    Value = option.Value,
                });
            }

            created++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, templateId.ToString(), "VariantsGenerated",
            null, new { template.Name, Created = created, Total = existingLabels.Count }, cancellationToken);
        return await BuildDetailAsync(template, cancellationToken);
    }

    public async Task<IssuedItemVariantDto?> UpdateVariantAsync(
        Guid templateId, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        var variant = await FindVariantAsync(templateId, variantId, cancellationToken);
        if (template is null || variant is null)
        {
            return null;
        }

        var (label, values) = await ResolveLabelAndValuesAsync(templateId, request, cancellationToken);
        var duplicate = await _dbContext.IssuedItemVariants.AnyAsync(
            v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId && v.Label == label && v.Id != variantId,
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("values", $"De variant \"{label}\" bestaat al.");
        }

        // Merge the attribute values IN PLACE per attribute (corrections wave §6): the old
        // delete-all + re-add left a soft-deleted row occupying the same
        // (TenantId, VariantId, AttributeDefinitionId) key, which violated the unique index on
        // PostgreSQL for every attribute-backed variant edit. Unchanged values stay untouched.
        var oldValues = await _dbContext.IssuedItemVariantValues
            .Where(v => v.TenantId == _tenantContext.TenantId && v.VariantId == variantId)
            .ToListAsync(cancellationToken);
        var oldByDefinition = oldValues.ToDictionary(v => v.AttributeDefinitionId);
        var mergedValues = new List<IssuedItemVariantValue>();
        foreach (var value in values)
        {
            if (oldByDefinition.Remove(value.AttributeDefinitionId, out var existing))
            {
                existing.AttributeOptionId = value.AttributeOptionId;
                existing.Value = value.Value;
                existing.AttributeNameSnapshot = value.AttributeNameSnapshot;
                mergedValues.Add(existing);
            }
            else
            {
                value.VariantId = variant.Id;
                _dbContext.IssuedItemVariantValues.Add(value);
                mergedValues.Add(value);
            }
        }

        // Attributes that no longer apply are removed (soft delete via the interceptor).
        _dbContext.RemoveRange(oldByDefinition.Values);

        var oldLabel = variant.Label;
        var oldThreshold = variant.LowStockThreshold;
        variant.Label = label;
        variant.IsActive = request.IsActive;
        variant.SortOrder = request.SortOrder;
        variant.LowStockThreshold = NonNegativeOrNull(request.LowStockThreshold);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!request.IsActive)
        {
            await _auditService.RecordAsync(VariantEntity, variant.Id.ToString(), "Archived",
                new { Label = oldLabel }, new { variant.Label }, cancellationToken);
        }
        else
        {
            await _auditService.RecordAsync(VariantEntity, variant.Id.ToString(), "Updated",
                new { Label = oldLabel, LowStockThreshold = oldThreshold },
                new { variant.Label, variant.LowStockThreshold }, cancellationToken);
        }

        return Map(variant, mergedValues.Select(MapValue).ToList());
    }

    public async Task<bool> DeleteVariantAsync(Guid templateId, Guid variantId, CancellationToken cancellationToken)
    {
        var variant = await FindVariantAsync(templateId, variantId, cancellationToken);
        if (variant is null)
        {
            return false;
        }

        if (variant.CurrentStock != 0)
        {
            throw new DomainValidationException("Deze variant heeft nog voorraad. Corrigeer de voorraad naar 0 of archiveer de variant.");
        }

        _dbContext.Remove(variant); // soft delete; issued items keep their VariantSnapshot
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(VariantEntity, variant.Id.ToString(), "Deleted", new { variant.Label }, null, cancellationToken);
        return true;
    }

    // --- Stock ---

    public async Task<StockMovementDto?> ReceiveStockAsync(Guid templateId, StockReceiptRequest request, CancellationToken cancellationToken)
    {
        var (template, variant) = await ResolveStockTargetAsync(templateId, request.VariantId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        if (request.Quantity <= 0)
        {
            throw new DomainValidationException("quantity", "De ontvangen hoeveelheid moet groter dan 0 zijn.");
        }

        var hasMovements = await _dbContext.StockMovements.AnyAsync(
            m => m.TenantId == _tenantContext.TenantId && m.TemplateId == templateId && m.VariantId == request.VariantId,
            cancellationToken);
        var type = hasMovements ? StockMovementType.Purchase : StockMovementType.InitialStock;

        var movement = ApplyMovement(template, variant, type, request.Quantity, null, Trim(request.Notes), null);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(MovementEntity, movement.Id.ToString(), "StockReceived",
            null, new { template.Name, variant?.Label, movement.Quantity, movement.ResultingStock }, cancellationToken);
        return await MapMovementAsync(movement, cancellationToken);
    }

    public async Task<StockMovementDto?> CorrectStockAsync(Guid templateId, StockCorrectionRequest request, CancellationToken cancellationToken)
    {
        var (template, variant) = await ResolveStockTargetAsync(templateId, request.VariantId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Een reden is verplicht bij een voorraadcorrectie.");
        }

        if (request.NewQuantity < 0 && !template.AllowNegativeStock)
        {
            throw new DomainValidationException("newQuantity", "Negatieve voorraad is niet toegestaan voor dit sjabloon.");
        }

        var current = variant?.CurrentStock ?? template.CurrentStock;
        var delta = request.NewQuantity - current;
        if (delta == 0)
        {
            throw new DomainValidationException("newQuantity", "De nieuwe voorraad is gelijk aan de huidige voorraad.");
        }

        var movement = ApplyMovement(template, variant, StockMovementType.Correction, delta, request.Reason.Trim(), null, null);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(MovementEntity, movement.Id.ToString(), "StockCorrected",
            new { Previous = current }, new { template.Name, variant?.Label, movement.ResultingStock, movement.Reason }, cancellationToken);
        if (_lowStockNotifier is not null)
        {
            await _lowStockNotifier.NotifyIfCrossedAsync(template, variant, current, cancellationToken);
        }

        return await MapMovementAsync(movement, cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovementDto>?> ListMovementsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var movements = await _dbContext.StockMovements.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId && m.TemplateId == templateId)
            .OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var variantLabels = await VariantLabelsAsync(templateId, cancellationToken);
        var employeeNames = await EmployeeNamesAsync(movements.Where(m => m.EmployeeId.HasValue).Select(m => m.EmployeeId!.Value), cancellationToken);
        return movements
            .Select(m => Map(m,
                m.VariantId is { } variantId ? variantLabels.GetValueOrDefault(variantId) : null,
                m.EmployeeId is { } employeeId ? employeeNames.GetValueOrDefault(employeeId) : null))
            .ToList();
    }

    public async Task<IReadOnlyList<CurrentHolderDto>?> ListCurrentHoldersAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        return await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.TemplateId == templateId && i.Status == IssuedItemStatus.Issued)
            .Join(_dbContext.Employees.Where(e => e.TenantId == _tenantContext.TenantId),
                item => item.EmployeeId, employee => employee.Id,
                (item, employee) => new CurrentHolderDto(
                    employee.Id, employee.FirstName + " " + employee.LastName, employee.EmployeeNumber,
                    item.Id, item.VariantSnapshot, item.Quantity, item.IssuedDate, item.SerialNumber))
            .OrderBy(h => h.EmployeeName)
            .ToListAsync(cancellationToken);
    }

    public StockMovement ApplyMovement(
        IssuedItemTemplate template, IssuedItemVariant? variant, StockMovementType type,
        int quantity, string? reason, string? notes, Guid? employeeId)
    {
        int resulting;
        if (variant is not null)
        {
            variant.CurrentStock += quantity;
            variant.Version = Guid.NewGuid(); // concurrency guard: conflicting writers fail on save
            resulting = variant.CurrentStock;
        }
        else
        {
            template.CurrentStock += quantity;
            template.Version = Guid.NewGuid();
            resulting = template.CurrentStock;
        }

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TemplateId = template.Id,
            VariantId = variant?.Id,
            MovementType = type,
            Quantity = quantity,
            ResultingStock = resulting,
            Reason = reason,
            Notes = notes,
            EmployeeId = employeeId,
            PerformedByUserId = _currentUser.CurrentUserId,
            Timestamp = DateTime.UtcNow,
        };
        _dbContext.StockMovements.Add(movement);
        return movement;
    }

    // --- Helpers ---

    private async Task<IssuedItemTemplateDetailDto> BuildDetailAsync(IssuedItemTemplate template, CancellationToken cancellationToken)
    {
        var templateAttributes = await _dbContext.IssuedItemTemplateAttributes.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.TemplateId == template.Id)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(cancellationToken);
        var definitionIds = templateAttributes.Select(a => a.AttributeDefinitionId).ToList();
        var definitions = await _dbContext.IssuedItemAttributeDefinitions.AsNoTracking()
            .Where(d => d.TenantId == _tenantContext.TenantId && definitionIds.Contains(d.Id))
            .ToListAsync(cancellationToken);
        var options = await _dbContext.IssuedItemAttributeOptions.AsNoTracking()
            .Where(o => o.TenantId == _tenantContext.TenantId && definitionIds.Contains(o.AttributeDefinitionId))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Value)
            .ToListAsync(cancellationToken);
        var optionLookup = options.ToLookup(o => o.AttributeDefinitionId);
        var orderedDefinitions = templateAttributes
            .Select(a => definitions.FirstOrDefault(d => d.Id == a.AttributeDefinitionId))
            .Where(d => d is not null)
            .Select(d => Map(d!, optionLookup[d!.Id]))
            .ToList();

        var variants = await _dbContext.IssuedItemVariants.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == template.Id)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Label)
            .ToListAsync(cancellationToken);
        var variantIds = variants.Select(v => v.Id).ToList();
        var values = await _dbContext.IssuedItemVariantValues.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && variantIds.Contains(v.VariantId))
            .ToListAsync(cancellationToken);
        var valueLookup = values.ToLookup(v => v.VariantId);
        var variantDtos = variants
            .Select(v => Map(v, valueLookup[v.Id].Select(MapValue).ToList()))
            .ToList();

        return new IssuedItemTemplateDetailDto(
            IssuedItemService.MapTemplate(template, variantDtos.Where(v => v.IsActive).Sum(v => v.CurrentStock), variantDtos.Count),
            orderedDefinitions, variantDtos);
    }

    private static int? NonNegativeOrNull(int? value) => value is { } v ? Math.Max(0, v) : null;

    /// <summary>
    /// Templates without linked attributes use free variant labels ("Small", "maat 43",
    /// "zwart"); templates with attributes keep the generated attribute-combination label.
    /// </summary>
    private async Task<(string Label, List<IssuedItemVariantValue> Values)> ResolveLabelAndValuesAsync(
        Guid templateId, SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var hasAttributes = await _dbContext.IssuedItemTemplateAttributes
            .AnyAsync(a => a.TenantId == _tenantContext.TenantId && a.TemplateId == templateId, cancellationToken);
        if (!hasAttributes)
        {
            var label = request.Label?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                throw new DomainValidationException("label",
                    "Geef een variantnaam of uitvoering op (bv. Small, maat 43, zwart).");
            }

            return (label, []);
        }

        return await ResolveVariantValuesAsync(templateId, request.Values, cancellationToken);
    }

    private async Task<(string Label, List<IssuedItemVariantValue> Values)> ResolveVariantValuesAsync(
        Guid templateId, IReadOnlyList<SaveVariantValueRequest> requested, CancellationToken cancellationToken)
    {
        var templateAttributes = await _dbContext.IssuedItemTemplateAttributes
            .Where(a => a.TenantId == _tenantContext.TenantId && a.TemplateId == templateId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(cancellationToken);
        if (templateAttributes.Count == 0)
        {
            throw new DomainValidationException("Koppel eerst attributen aan het sjabloon voordat je varianten aanmaakt.");
        }

        var definitionIds = templateAttributes.Select(a => a.AttributeDefinitionId).ToList();
        var definitions = await _dbContext.IssuedItemAttributeDefinitions
            .Where(d => d.TenantId == _tenantContext.TenantId && definitionIds.Contains(d.Id))
            .ToListAsync(cancellationToken);

        var parts = new List<string>();
        var values = new List<IssuedItemVariantValue>();
        foreach (var templateAttribute in templateAttributes)
        {
            var definition = definitions.FirstOrDefault(d => d.Id == templateAttribute.AttributeDefinitionId)
                ?? throw new DomainValidationException("values", "Een attribuut van het sjabloon bestaat niet meer.");
            var request = requested.FirstOrDefault(v => v.AttributeDefinitionId == definition.Id)
                ?? throw new DomainValidationException("values", $"Waarde voor attribuut \"{definition.Name}\" ontbreekt.");

            string value;
            Guid? optionId = null;
            if (request.AttributeOptionId is { } requestedOptionId)
            {
                var option = await _dbContext.IssuedItemAttributeOptions.FirstOrDefaultAsync(
                    o => o.TenantId == _tenantContext.TenantId && o.AttributeDefinitionId == definition.Id && o.Id == requestedOptionId,
                    cancellationToken)
                    ?? throw new DomainValidationException("values", $"De gekozen waarde voor \"{definition.Name}\" bestaat niet.");
                value = option.Value;
                optionId = option.Id;
            }
            else
            {
                if (!definition.AllowCustomValues)
                {
                    throw new DomainValidationException("values", $"Attribuut \"{definition.Name}\" laat geen vrije waarden toe.");
                }

                value = RequireValue(request.CustomValue);
            }

            parts.Add(value);
            values.Add(new IssuedItemVariantValue
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                AttributeDefinitionId = definition.Id,
                AttributeNameSnapshot = definition.Name,
                AttributeOptionId = optionId,
                Value = value,
            });
        }

        return (string.Join(" / ", parts), values);
    }

    private async Task<(IssuedItemTemplate? Template, IssuedItemVariant? Variant)> ResolveStockTargetAsync(
        Guid templateId, Guid? variantId, CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(templateId, cancellationToken);
        if (template is null)
        {
            return (null, null);
        }

        if (!template.StockTrackingEnabled)
        {
            throw new DomainValidationException("Voorraadbeheer staat uit voor dit sjabloon.");
        }

        if (template.VariantsEnabled)
        {
            if (variantId is null)
            {
                throw new DomainValidationException("variantId", "Kies een variant: de voorraad wordt per variant bijgehouden.");
            }

            var variant = await FindVariantAsync(templateId, variantId.Value, cancellationToken)
                ?? throw new DomainValidationException("variantId", "De variant bestaat niet.");
            return (template, variant);
        }

        if (variantId is not null)
        {
            throw new DomainValidationException("variantId", "Dit sjabloon gebruikt geen varianten.");
        }

        return (template, null);
    }

    private Task<IssuedItemTemplate?> FindTemplateAsync(Guid templateId, CancellationToken cancellationToken) =>
        _dbContext.IssuedItemTemplates.FirstOrDefaultAsync(
            t => t.TenantId == _tenantContext.TenantId && t.Id == templateId, cancellationToken);

    private Task<IssuedItemVariant?> FindVariantAsync(Guid templateId, Guid variantId, CancellationToken cancellationToken) =>
        _dbContext.IssuedItemVariants.FirstOrDefaultAsync(
            v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId && v.Id == variantId, cancellationToken);

    private Task<IssuedItemAttributeDefinition?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.IssuedItemAttributeDefinitions.FirstOrDefaultAsync(
            d => d.TenantId == _tenantContext.TenantId && d.Id == id, cancellationToken);

    private async Task<IReadOnlyList<IssuedItemAttributeOptionDto>> ListOptionsAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var options = await _dbContext.IssuedItemAttributeOptions.AsNoTracking()
            .Where(o => o.TenantId == _tenantContext.TenantId && o.AttributeDefinitionId == definitionId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Value)
            .ToListAsync(cancellationToken);
        return options.Select(Map).ToList();
    }

    private async Task<Dictionary<Guid, string>> VariantLabelsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        return await _dbContext.IssuedItemVariants.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.TemplateId == templateId)
            .ToDictionaryAsync(v => v.Id, v => v.Label, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> EmployeeNamesAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, cancellationToken);
    }

    private async Task<StockMovementDto> MapMovementAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        string? variantLabel = null;
        if (movement.VariantId is { } variantId)
        {
            variantLabel = await _dbContext.IssuedItemVariants.AsNoTracking()
                .Where(v => v.TenantId == _tenantContext.TenantId && v.Id == variantId)
                .Select(v => v.Label)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Map(movement, variantLabel, null);
    }

    private static void ApplyDefinition(IssuedItemAttributeDefinition definition, SaveAttributeDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        definition.Name = request.Name.Trim();
        definition.AllowCustomValues = request.AllowCustomValues;
        definition.IsShared = request.IsShared;
        definition.SortOrder = request.SortOrder;
        definition.IsActive = request.IsActive;
    }

    private static string RequireValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("value", "De waarde is verplicht.");
        }

        return value.Trim();
    }

    private static IssuedItemAttributeDefinitionDto Map(IssuedItemAttributeDefinition d, IEnumerable<IssuedItemAttributeOption> options) =>
        new(d.Id, d.Name, d.AllowCustomValues, d.IsShared, d.SortOrder, d.IsActive, options.Select(Map).ToList());

    private static IssuedItemAttributeOptionDto Map(IssuedItemAttributeOption o) => new(o.Id, o.Value, o.SortOrder, o.IsActive);

    private static IssuedItemVariantDto Map(IssuedItemVariant v, IReadOnlyList<IssuedItemVariantValueDto> values) =>
        new(v.Id, v.Label, v.CurrentStock, v.IsActive, v.SortOrder, values, v.LowStockThreshold);

    private static IssuedItemVariantValueDto MapValue(IssuedItemVariantValue v) =>
        new(v.AttributeDefinitionId, v.AttributeNameSnapshot, v.AttributeOptionId, v.Value);

    private static StockMovementDto Map(StockMovement m, string? variantLabel, string? employeeName) => new(
        m.Id, m.TemplateId, m.VariantId, variantLabel, m.MovementType, m.Quantity, m.ResultingStock,
        m.Reason, m.Notes, m.EmployeeId, employeeName, m.PerformedByUserId, m.Timestamp);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
