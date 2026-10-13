using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// One employee's yearly entitlement inputs for a single balance type. Used and pending days
/// are never stored here — they are computed live from approved/pending absences. Base
/// entitlement and carry-over are set by HR (audited); manual adjustments live in the ledger.
/// Unique per (tenant, employee, calendar year, balance type).
/// </summary>
public class EmployeeLeaveBalance : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }
    public int CalendarYear { get; set; }
    public Guid BalanceTypeId { get; set; }

    public decimal BaseEntitlementDays { get; set; }
    public decimal CarryOverDays { get; set; }
}
