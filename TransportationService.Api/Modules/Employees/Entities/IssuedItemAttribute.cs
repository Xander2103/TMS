using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// Tenant-level attribute definition for issued-item variants (Maat, Kleur, Opslag, Model, ...).
/// Shared definitions are reusable master data available to any template; template-specific
/// ones (IsShared = false) exist for genuinely bespoke attributes.
/// </summary>
public class IssuedItemAttributeDefinition : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Allows variants to carry a free-text value next to the predefined options.</summary>
    public bool AllowCustomValues { get; set; }

    /// <summary>True = reusable master attribute; false = intended for a single template.</summary>
    public bool IsShared { get; set; } = true;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Predefined value for an attribute definition (Maat → XS/S/M/L/XL).</summary>
public class IssuedItemAttributeOption : AuditableTenantEntity
{
    public Guid AttributeDefinitionId { get; set; }
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Join row selecting which attribute definitions a template uses, in which order.</summary>
public class IssuedItemTemplateAttribute : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public int SortOrder { get; set; }
}
