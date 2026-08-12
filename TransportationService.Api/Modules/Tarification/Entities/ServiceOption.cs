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

    /// <summary>Sales code for service lines of this option (Wave 2); wins over rule/agreement codes.</summary>
    public Guid? SalesCategoryId { get; set; }

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
/// Condition kinds a service option can be restricted by, beyond the existing ADR flag.
/// Evaluation model (documented in docs/pricing.md): rows of the SAME kind are OR'ed (any
/// selected warehouse matches), DIFFERENT kinds — including OnlyForAdr — are AND'ed. No rows =
/// the service applies to all orders. The enum is deliberately extensible: Product /
/// ProductCategory / CustomerOwnedStock conditions require a cargo product master that does
/// not exist yet in this codebase and MUST NOT be faked with loose category names.
/// </summary>
public enum ServiceConditionKind
{
    /// <summary>The order touches the referenced warehouse (a stop at the warehouse's master location).</summary>
    Warehouse = 0,

    /// <summary>
    /// Wave 2026-08-04 §16: a stop's time requirement promises execution at or before
    /// <see cref="ServiceOptionCondition.TimeOfDay"/> (e.g. "Levering vóór 10u").
    /// </summary>
    StopTimeBefore = 1,

    /// <summary>Wave 2026-08-04 §16: a stop's time requirement starts at or after TimeOfDay (avondlevering).</summary>
    StopTimeAfter = 2,

    /// <summary>Wave 2026-08-04 §16: a matching stop has "Afspraak verplicht".</summary>
    AppointmentRequired = 3,

    /// <summary>Wave 2026-08-04 §16: a matching stop is planned on a Saturday or Sunday.</summary>
    Weekend = 4,

    /// <summary>Wave 3 §4: a matching stop is planned on a tenant-configured holiday (tenant_holidays).</summary>
    Holiday = 5,

    // P6: equipment/movement/activity conditions (order-level flags, not stop-scoped).
    /// <summary>The order requires a crane (TransportOrder.CraneRequired).</summary>
    Crane = 6,

    /// <summary>The order requires a plateau/flatbed (TransportOrder.PlateauRequired).</summary>
    Plateau = 7,

    /// <summary>The order requires a Moffett/kooiaap (TransportOrder.MoffettRequired).</summary>
    Moffett = 8,

    /// <summary>The order is a return movement (TransportOrder.IsReturnMovement).</summary>
    ReturnMovement = 9,

    /// <summary>The order's linked dossier activity has the type in <see cref="ServiceOptionCondition.ActivityTypeId"/>.</summary>
    ActivityType = 10,
}

/// <summary>Which stops a time-based condition looks at (wave 2026-08-04 §16).</summary>
public enum ServiceConditionStopScope
{
    Any = 0,
    Loading = 1,
    Unloading = 2,
}

/// <summary>One condition row restricting when a service option is charged/auto-applied.</summary>
public class ServiceOptionCondition : AuditableTenantEntity
{
    public Guid ServiceOptionId { get; set; }
    public ServiceConditionKind Kind { get; set; }

    /// <summary>The referenced master-data id (Kind == Warehouse: the Warehouse id). Unused (Guid.Empty) for time kinds.</summary>
    public Guid ReferenceId { get; set; }

    /// <summary>Time kinds: which stops this condition looks at.</summary>
    public ServiceConditionStopScope StopScope { get; set; } = ServiceConditionStopScope.Any;

    /// <summary>StopTimeBefore/StopTimeAfter: the configured threshold (never hardcoded).</summary>
    public TimeOnly? TimeOfDay { get; set; }

    /// <summary>Kind == ActivityType: the dossier activity type this condition matches (P6).</summary>
    public Guid? ActivityTypeId { get; set; }

    /// <summary>
    /// Wave 2026-08-04 §17: among competing matched StopTimeBefore (or StopTimeAfter) options
    /// the highest priority wins; on a tie the most specific time wins; an exact tie is a
    /// blocking configuration error.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>§17: opt-in stacking — this condition never competes, it always applies when matched.</summary>
    public bool AllowStacking { get; set; }
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
