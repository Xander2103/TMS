using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>Central stock status; rules live in InventoryStatusCalculator.</summary>
public enum InventoryStatus
{
    Normal,
    LowStock,
    CriticalStock,
    OutOfStock,
    NegativeStock,
}

public enum InventoryAlertStatus
{
    Active,
    Resolved,
}

/// <summary>
/// One row per stock target (template, or template+variant): the current non-normal stock
/// condition. Upserted on every stock mutation and by the inventory sweep; a recovery marks
/// the row Resolved (and resolves the linked notifications), a later new drop re-activates
/// it and may notify again. This is the anti-spam state, not a log — the ledger is
/// <see cref="StockMovement"/>.
/// </summary>
public class InventoryAlert : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid? VariantId { get; set; }

    /// <summary>Current condition (never Normal: normal targets have no active alert).</summary>
    public InventoryStatus Kind { get; set; }

    public InventoryAlertStatus Status { get; set; } = InventoryAlertStatus.Active;

    public int StockSnapshot { get; set; }
    public int? WarningSnapshot { get; set; }
    public int? MinimumSnapshot { get; set; }

    public DateTime LastSeenAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
