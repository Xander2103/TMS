using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Hr.Entities;

public enum LeaveAdjustmentKind
{
    Grant,
    Seniority,
    Correction,
    Override,
}

/// <summary>
/// Append-only manual-adjustment record against an <see cref="EmployeeLeaveBalance"/>. Manual
/// changes never overwrite history — corrections are new rows. Actor/timestamp come from the
/// audit fields; the sum of a balance's adjustments is its ManualAdjustmentDays.
/// </summary>
public class LeaveBalanceAdjustment : AuditableTenantEntity
{
    public Guid EmployeeLeaveBalanceId { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveAdjustmentKind Kind { get; set; } = LeaveAdjustmentKind.Grant;
}
