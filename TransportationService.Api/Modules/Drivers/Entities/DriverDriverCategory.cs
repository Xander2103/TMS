using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Drivers.Entities;

/// <summary>
/// Join: driver → operational driver category (multi). The legacy single
/// Driver.DriverCategoryId mirrors the primary (first) category for backward
/// compatibility; this table is authoritative. Licence categories (B/C/CE) are NOT
/// modeled here — they remain qualifications feeding the eligibility engine.
/// </summary>
public class DriverDriverCategory : AuditableTenantEntity
{
    public Guid DriverId { get; set; }
    public Guid DriverCategoryId { get; set; }

    /// <summary>Selection order; the priority-0 row is the primary category.</summary>
    public int SortOrder { get; set; }
}
