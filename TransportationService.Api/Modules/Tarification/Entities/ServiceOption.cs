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

    /// <summary>Percent of the base subtotal or a fixed amount.</summary>
    public SurchargeKind Kind { get; set; } = SurchargeKind.Fixed;

    public decimal DefaultValue { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Customer-specific price for a service option (overrides the default value).</summary>
public class CustomerServiceOptionPrice : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid ServiceOptionId { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Units a customer commonly uses; shown first during order entry for that customer.</summary>
public class CustomerPreferredUnit : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid UnitTypeId { get; set; }
    public int SortOrder { get; set; }
}
