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

    /// <summary>Grouping label (Algemeen, Chauffeur, Magazijn, Klasse 7, Optioneel, ...). Kept as
    /// snapshot of the master-data category name so historical rows keep their label.</summary>
    public string Category { get; set; } = "Algemeen";

    /// <summary>Master-data category (issued_item_categories); null on legacy rows.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>CSV of JobFunction codes this item applies to; empty = everyone.</summary>
    public string? ApplicableJobFunctionCodes { get; set; }

    public int DefaultQuantity { get; set; } = 1;
    public bool RequiresSerialNumber { get; set; }
    public bool RequiresReceivedDate { get; set; } = true;
    public bool ReturnRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string? Description { get; set; }

    /// <summary>Unit label for stock counts (stuks, paar, set, ...).</summary>
    public string? Unit { get; set; }

    public string? Notes { get; set; }

    /// <summary>Off = issuance never touches inventory (behaviour of templates before stock existed).</summary>
    public bool StockTrackingEnabled { get; set; }

    /// <summary>On = stock lives on the variants; issuance requires a variant choice.</summary>
    public bool VariantsEnabled { get; set; }

    /// <summary>
    /// Off = a mutation may never push stock below zero. On = it may, but only through the
    /// explicit confirmation flow (override permission + fresh Version + reason when
    /// <see cref="NegativeStockRequiresReason"/> is set).
    /// </summary>
    public bool AllowNegativeStock { get; set; }

    /// <summary>Warning level: status LowStock when stock drops to or below this value.</summary>
    public int? LowStockThreshold { get; set; }

    /// <summary>Minimum level: status CriticalStock when stock drops to or below this value.</summary>
    public int? MinimumStock { get; set; }

    /// <summary>Desired level; reorder proposals suggest TargetStockLevel − current stock.</summary>
    public int? TargetStockLevel { get; set; }

    /// <summary>Pack/order size: suggested reorder quantities round up to a multiple of this.</summary>
    public int? ReorderQuantity { get; set; }

    /// <summary>Require a reason when a confirmed mutation pushes stock below zero.</summary>
    public bool NegativeStockRequiresReason { get; set; } = true;

    public string? StorageLocation { get; set; }

    /// <summary>
    /// Cached aggregate of the template's stock movements; used only when the template has
    /// no variants. Mutated exclusively inside the movement transaction.
    /// </summary>
    public int CurrentStock { get; set; }

    /// <summary>Optimistic-concurrency token; rotated on every stock mutation.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
