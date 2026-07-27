import { apiClient } from '../../../api/apiClient'
import type {
  EmployeeLeaveBalance,
  LeaveAdjustment,
  LeaveAdjustmentKind,
  LeaveBalanceType,
  LeaveSettings,
  LeaveType,
} from '../types'

export function getEmployeeLeaveBalance(employeeId: string, year: number): Promise<EmployeeLeaveBalance> {
  return apiClient.getJson<EmployeeLeaveBalance>(`/api/employees/${employeeId}/leave-balance?year=${year}`)
}

export interface SetEntitlementInput {
  balanceTypeId: string
  baseEntitlementDays: number
  carryOverDays: number
  /** Optional reason shown in the personnel history (e.g. 'Jaarlijks saldo toegekend'). */
  reason?: string | null
}

export function setLeaveEntitlement(employeeId: string, year: number, input: SetEntitlementInput): Promise<EmployeeLeaveBalance> {
  return apiClient.putJson<EmployeeLeaveBalance, SetEntitlementInput>(`/api/employees/${employeeId}/leave-balance?year=${year}`, input)
}

export interface AddAdjustmentInput {
  balanceTypeId: string
  days: number
  reason: string
  kind: LeaveAdjustmentKind
}

export function addLeaveAdjustment(employeeId: string, year: number, input: AddAdjustmentInput): Promise<EmployeeLeaveBalance> {
  return apiClient.postJson<EmployeeLeaveBalance, AddAdjustmentInput>(`/api/employees/${employeeId}/leave-balance/adjustments?year=${year}`, input)
}

export function getLeaveAdjustments(employeeId: string, year: number, balanceTypeId: string): Promise<LeaveAdjustment[]> {
  return apiClient.getJson<LeaveAdjustment[]>(`/api/employees/${employeeId}/leave-balance/adjustments?year=${year}&balanceTypeId=${balanceTypeId}`)
}

export function getLeaveBalanceTypes(): Promise<LeaveBalanceType[]> {
  return apiClient.getJson<LeaveBalanceType[]>('/api/leave-balance-types')
}

export function getLeaveTypes(params?: { activeOnly?: boolean; selfServiceOnly?: boolean }): Promise<LeaveType[]> {
  const query = new URLSearchParams()
  if (params?.activeOnly) query.set('activeOnly', 'true')
  if (params?.selfServiceOnly) query.set('selfServiceOnly', 'true')
  const q = query.toString()
  return apiClient.getJson<LeaveType[]>(`/api/leave-types${q ? `?${q}` : ''}`)
}

export type SaveLeaveBalanceTypeInput = Omit<LeaveBalanceType, 'id'>
export function saveLeaveBalanceType(id: string | null, input: SaveLeaveBalanceTypeInput): Promise<LeaveBalanceType> {
  return id
    ? apiClient.putJson<LeaveBalanceType, SaveLeaveBalanceTypeInput>(`/api/leave-balance-types/${id}`, input)
    : apiClient.postJson<LeaveBalanceType, SaveLeaveBalanceTypeInput>('/api/leave-balance-types', input)
}

/** Deletes an UNUSED balance type; a used one is refused with the deactivate-instead message. */
export function deleteLeaveBalanceType(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/leave-balance-types/${id}`)
}

/** Deletes an UNUSED leave type; one referenced by any absence is refused with the deactivate-instead message. */
export function deleteLeaveType(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/leave-types/${id}`)
}

export type SaveLeaveTypeInput = Omit<LeaveType, 'id'>
export function saveLeaveType(id: string | null, input: SaveLeaveTypeInput): Promise<LeaveType> {
  return id
    ? apiClient.putJson<LeaveType, SaveLeaveTypeInput>(`/api/leave-types/${id}`, input)
    : apiClient.postJson<LeaveType, SaveLeaveTypeInput>('/api/leave-types', input)
}

export function getLeaveSettings(): Promise<LeaveSettings> {
  return apiClient.getJson<LeaveSettings>('/api/hr/leave-settings')
}

export function updateLeaveSettings(input: LeaveSettings): Promise<LeaveSettings> {
  return apiClient.putJson<LeaveSettings, LeaveSettings>('/api/hr/leave-settings', input)
}

export function getMyLeaveBalance(year: number): Promise<EmployeeLeaveBalance> {
  return apiClient.getJson<EmployeeLeaveBalance>(`/api/me/leave-balance?year=${year}`)
}
