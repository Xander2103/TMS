using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

/// <summary>
/// Per-tenant leave-balance defaults (one row per tenant, lazily created). HR still overrides
/// entitlement per employee/year/balance type. The automated year-end carry-over process and
/// carry-over expiry are a documented follow-up; the settings seam is present.
/// </summary>
public class LeaveEntitlementSettings : AuditableTenantEntity
{
    /// <summary>Default statutory annual entitlement (days) when no per-employee row exists.</summary>
    public decimal DefaultAnnualEntitlementDays { get; set; } = 20m;
    /// <summary>When true, pending (Requested/UnderReview) leave reduces the remaining balance.</summary>
    public bool PendingReservesBalance { get; set; } = true;
    /// <summary>When false, a self-service request that would go below zero is blocked.</summary>
    public bool AllowNegativeBalance { get; set; }
    public bool CarryOverEnabled { get; set; } = true;
    /// <summary>Optional cap on manual carry-over days per balance/year.</summary>
    public decimal? MaxCarryOverDays { get; set; }
}
