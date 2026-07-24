using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A delivery zone for zone-based pricing (Z1, Z2, ...). Zones are matched on the delivery
/// address country + postal code against the configured areas.
/// </summary>
public class PricingZone : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<PricingZoneArea> Areas { get; set; } = new();
}

/// <summary>Postal-code range belonging to a zone; From/To compare numerically when possible.</summary>
public class PricingZoneArea : AuditableTenantEntity
{
    public Guid ZoneId { get; set; }
    public string CountryCode { get; set; } = "BE";
    public string PostalCodeFrom { get; set; } = string.Empty;
    public string PostalCodeTo { get; set; } = string.Empty;
}
