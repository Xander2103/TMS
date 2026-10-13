using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// Wave 3 §4: a tenant-configured holiday date. Exists purely to drive time surcharges — a
/// service option with a <see cref="ServiceConditionKind.Holiday"/> condition applies when the
/// stop's date is in this table. Managed on tariffs.manage under the pricing settings.
/// </summary>
public class TenantHoliday : AuditableTenantEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
}
