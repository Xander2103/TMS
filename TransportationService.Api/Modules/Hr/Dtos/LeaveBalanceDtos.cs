using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Dtos;

/// <summary>One balance type's computed figures for an employee/year.</summary>
public sealed record LeaveBalanceRowDto(
    Guid BalanceTypeId,
    string BalanceTypeCode,
    string BalanceTypeName,
    decimal BaseEntitlementDays,
    decimal CarryOverDays,
    decimal ManualAdjustmentDays,
    decimal ApprovedUsedDays,
    decimal PendingReservedDays,
    decimal RemainingDays,
    bool PendingReserves);

public sealed record EmployeeLeaveBalanceDto(Guid EmployeeId, int Year, IReadOnlyList<LeaveBalanceRowDto> Rows);

public sealed record LeaveAdjustmentDto(
    Guid Id, decimal Days, string Reason, LeaveAdjustmentKind Kind, DateTime CreatedAt, Guid? CreatedByUserId);

/// <summary>Outcome of the self-service over-request check for a leave request.</summary>
public sealed record LeaveAvailabilityResult(bool Allowed, decimal RemainingDays, decimal RequestedDays, string? Message);

public sealed record SetLeaveEntitlementRequest(
    Guid BalanceTypeId, decimal BaseEntitlementDays, decimal CarryOverDays,
    /// <summary>Optional reason shown in the entitlement history (e.g. "Jaarlijks saldo 2027 toegekend").</summary>
    string? Reason = null);

public sealed record AddLeaveAdjustmentRequest(Guid BalanceTypeId, decimal Days, string Reason, LeaveAdjustmentKind Kind);

public sealed record LeaveBalanceTypeDto(Guid Id, string Code, string Name, string? Description, bool IsActive, int SortOrder);

public sealed record SaveLeaveBalanceTypeRequest(string Code, string Name, string? Description, bool IsActive, int SortOrder);

public sealed record LeaveTypeDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive, bool IsPaid,
    bool DeductsFromBalance, Guid? BalanceTypeId, AbsenceType AbsenceType, bool RequiresApproval,
    bool AllowsHalfDays, bool RequiresReason, bool RequiresAttachment, bool VisibleInSelfService,
    string? Colour, int SortOrder);

public sealed record SaveLeaveTypeRequest(
    string Code, string Name, string? Description, bool IsActive, bool IsPaid,
    bool DeductsFromBalance, Guid? BalanceTypeId, AbsenceType AbsenceType, bool RequiresApproval,
    bool AllowsHalfDays, bool RequiresReason, bool RequiresAttachment, bool VisibleInSelfService,
    string? Colour, int SortOrder);

public sealed record LeaveSettingsDto(
    decimal DefaultAnnualEntitlementDays, bool PendingReservesBalance, bool AllowNegativeBalance,
    bool CarryOverEnabled, decimal? MaxCarryOverDays);
