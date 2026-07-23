using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// Concrete attribute combination of a template ("M / Zwart"). Owns the cached stock count
/// when the template tracks variants; <see cref="StockMovement"/> rows are the source of
/// truth and the cache is only mutated inside the movement transaction, guarded by
/// <see cref="Version"/> as optimistic-concurrency token.
/// </summary>
public class IssuedItemVariant : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }

    /// <summary>Display label generated from the attribute values, e.g. "M / Zwart".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Cached aggregate of the variant's stock movements.</summary>
    public int CurrentStock { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Optimistic-concurrency token; rotated on every stock mutation.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}

/// <summary>
/// One attribute value of a variant. References the master option when chosen from the
/// predefined values; carries a free Value for custom entries. AttributeNameSnapshot keeps
/// the combination readable even after a definition is renamed or removed.
/// </summary>
public class IssuedItemVariantValue : AuditableTenantEntity
{
    public Guid VariantId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public string AttributeNameSnapshot { get; set; } = string.Empty;
    public Guid? AttributeOptionId { get; set; }
    public string Value { get; set; } = string.Empty;
}
