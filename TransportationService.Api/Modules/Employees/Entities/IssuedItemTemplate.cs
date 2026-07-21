using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// Configurable template for an item issued to employees (badge, PDA, tankkaart, PBM, ...).
/// Managed in Settings. Employee records snapshot the name/category so later template
/// changes never mutate historical employee records.
/// </summary>
public class IssuedItemTemplate : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Grouping label (Algemeen, Chauffeur, Magazijn, Klasse 7, Optioneel, ...).</summary>
    public string Category { get; set; } = "Algemeen";

    /// <summary>CSV of JobFunction codes this item applies to; empty = everyone.</summary>
    public string? ApplicableJobFunctionCodes { get; set; }

    public int DefaultQuantity { get; set; } = 1;
    public bool RequiresSerialNumber { get; set; }
    public bool RequiresReceivedDate { get; set; } = true;
    public bool ReturnRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
