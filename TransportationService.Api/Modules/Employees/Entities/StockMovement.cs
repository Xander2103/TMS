using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

public enum StockMovementType
{
    InitialStock,
    Purchase,
    Correction,
    Issue,
    Return,
    Damaged,
    Lost,
    Disposed,
    Transfer,

    /// <summary>WH-M5: a consumable handed out for good (stock leaves, nothing comes back).</summary>
    Consumption,
}

/// <summary>
/// Immutable stock ledger line for an issued-item template (or one of its variants). The
/// signed <see cref="Quantity"/> stream is the source of truth for current stock; cached
/// counts on template/variant are updated in the same transaction that appends the line.
/// Damaged/Lost/Disposed returns record a zero-quantity line: the goods left usable stock
/// at issue time and never come back into it.
/// </summary>
public class StockMovement : AuditableTenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid? VariantId { get; set; }

    public StockMovementType MovementType { get; set; }

    /// <summary>Signed stock delta (+ receipt/return, − issue, 0 for damaged/lost/disposed).</summary>
    public int Quantity { get; set; }

    /// <summary>Cached stock of the owning template/variant directly after this movement.</summary>
    public int ResultingStock { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Employee involved for issue/return movements.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>
    /// WH-M4: the issuance (employee_issued_items row) an issue/return/consumption belongs to, so
    /// the movement history can show the serial and the issuance it closed. Soft reference;
    /// null on receipts, corrections and legacy rows.
    /// </summary>
    public Guid? IssuedItemId { get; set; }

    public Guid? PerformedByUserId { get; set; }
    public DateTime Timestamp { get; set; }
}
