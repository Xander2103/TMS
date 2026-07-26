using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A configurable delivery service/supplement (Levering vóór 08:00, Laadklep, ADR, Wachttijd,
/// Zaterdaglevering, ...). Selected on an order it contributes to the calculated price and
/// later becomes a separate invoice line.
/// </summary>
public class ServiceOption : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Explicit pricing method: fixed amount, percent, per hour or per stop.</summary>
    public SurchargeKind Kind { get; set; } = SurchargeKind.Fixed;

    public decimal DefaultValue { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Internal description for admins.</summary>
    public string? Description { get; set; }

    /// <summary>Overrides the name on invoice lines when set.</summary>
    public string? InvoiceDescription { get; set; }

    /// <summary>Off = configured for invoicing only, never offered during order entry.</summary>
    public bool SelectableInOrders { get; set; } = true;

    /// <summary>Kind == PerUnit only: which managed unit this service counts (e.g. Colli, Pallet).</summary>
    public Guid? UnitTypeId { get; set; }

    /// <summary>The engine adds this service automatically, quantified from the order, without the
    /// customer/planner selecting it (a contract service, e.g. Picking, PAL UIT).</summary>
    public bool AutoApply { get; set; }

    /// <summary>Only charged/auto-applied when the order requires ADR; otherwise informational.</summary>
    public bool OnlyForAdr { get; set; }
}

/// <summary>
/// Customer-specific override of a global service option (spec §7): a different value, a
/// customer minimum, an own invoice description, an effective window, or disabling the
/// service for this customer entirely. No row (or an empty row) = the global default applies.
/// </summary>
public class CustomerServiceOptionPrice : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid ServiceOptionId { get; set; }

    /// <summary>Override value; null = inherit the global default value.</summary>
    public decimal? Value { get; set; }

    /// <summary>The service is globally available but switched off for this customer.</summary>
    public bool Disabled { get; set; }

    /// <summary>Customer-specific minimum on the computed service amount.</summary>
    public decimal? MinimumAmount { get; set; }

    /// <summary>Customer-specific invoice-line description.</summary>
    public string? InvoiceDescription { get; set; }

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }

    /// <summary>Overrides the global ServiceOption.AutoApply for this customer; null = inherit.</summary>
    public bool? AutoApplyOverride { get; set; }
}

/// <summary>
/// Customer-specific configuration of a global unit: which units the customer commonly uses
/// (shown first during order entry), how the customer names them and which external EDI/Excel
/// codes map onto them. The global unit is never duplicated per customer — this row only
/// carries the customer-specific presentation and mapping.
/// </summary>
public class CustomerPreferredUnit : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid UnitTypeId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Customer-facing label override (e.g. "EURO PAL"); null = global unit name.</summary>
    public string? CustomerLabel { get; set; }

    /// <summary>External unit code used in this customer's EDI messages (e.g. "EPAL").</summary>
    public string? EdiCode { get; set; }

    /// <summary>External unit code used in this customer's Excel/import files.</summary>
    public string? ExcelCode { get; set; }

    /// <summary>Favourites float to the very top of the order-entry unit selector.</summary>
    public bool IsFavourite { get; set; } = true;
}
