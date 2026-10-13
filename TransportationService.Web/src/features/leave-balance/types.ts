export type LeaveAdjustmentKind = 'Grant' | 'Seniority' | 'Correction' | 'Override'

/** Vertaalsleutels — renderen als t(LEAVE_ADJUSTMENT_KIND_LABELS[kind]). */
export const LEAVE_ADJUSTMENT_KIND_LABELS: Record<LeaveAdjustmentKind, string> = {
  Grant: 'leave.kind.Grant',
  Seniority: 'leave.kind.Seniority',
  Correction: 'leave.kind.Correction',
  Override: 'leave.kind.Override',
}

export type AbsenceTypeCode = 'Vacation' | 'Sick' | 'Training' | 'PersonalLeave' | 'Unpaid' | 'Other'

export interface LeaveBalanceRow {
  balanceTypeId: string
  balanceTypeCode: string
  balanceTypeName: string
  baseEntitlementDays: number
  carryOverDays: number
  manualAdjustmentDays: number
  approvedUsedDays: number
  pendingReservedDays: number
  remainingDays: number
  pendingReserves: boolean
}

export interface EmployeeLeaveBalance {
  employeeId: string
  year: number
  rows: LeaveBalanceRow[]
}

export interface LeaveAdjustment {
  id: string
  days: number
  reason: string
  kind: LeaveAdjustmentKind
  createdAt: string
  createdByUserId: string | null
}

export interface LeaveBalanceType {
  id: string
  code: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
}

export interface LeaveType {
  id: string
  code: string
  name: string
  description: string | null
  isActive: boolean
  isPaid: boolean
  deductsFromBalance: boolean
  balanceTypeId: string | null
  absenceType: AbsenceTypeCode
  requiresApproval: boolean
  allowsHalfDays: boolean
  requiresReason: boolean
  requiresAttachment: boolean
  visibleInSelfService: boolean
  colour: string | null
  sortOrder: number
}

export interface LeaveSettings {
  defaultAnnualEntitlementDays: number
  pendingReservesBalance: boolean
  allowNegativeBalance: boolean
  carryOverEnabled: boolean
  maxCarryOverDays: number | null
}
