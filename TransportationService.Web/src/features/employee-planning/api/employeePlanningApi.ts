import { apiClient } from '../../../api/apiClient'
import type { ScheduleGrid, Shift, ShiftInput, ShiftStatus } from '../types'

export function getSchedule(
  from: string,
  to: string,
  departmentId?: string,
  employeeId?: string,
): Promise<ScheduleGrid> {
  const query = new URLSearchParams({ from, to })
  if (departmentId) query.set('departmentId', departmentId)
  if (employeeId) query.set('employeeId', employeeId)
  return apiClient.getJson<ScheduleGrid>(`/api/employee-planning?${query.toString()}`)
}

export function getShift(id: string): Promise<Shift> {
  return apiClient.getJson<Shift>(`/api/shifts/${id}`)
}

export function createShift(input: ShiftInput): Promise<Shift> {
  return apiClient.postJson<Shift, ShiftInput>('/api/shifts', input)
}

export function updateShift(id: string, input: Omit<ShiftInput, 'employeeId'>): Promise<Shift> {
  return apiClient.putJson<Shift, Omit<ShiftInput, 'employeeId'>>(`/api/shifts/${id}`, input)
}

export function changeShiftStatus(id: string, status: ShiftStatus): Promise<Shift> {
  return apiClient.postJson<Shift, { status: ShiftStatus }>(`/api/shifts/${id}/status`, { status })
}

export function deleteShift(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/shifts/${id}`)
}

export interface CopyWeekResult {
  copiedCount: number
  skippedCount: number
}

export function copyWeek(
  sourceWeekStart: string,
  targetWeekStart: string,
  employeeId?: string,
  departmentId?: string,
): Promise<CopyWeekResult> {
  return apiClient.postJson<CopyWeekResult, object>('/api/shifts/copy-week', {
    sourceWeekStart,
    targetWeekStart,
    employeeId: employeeId ?? null,
    departmentId: departmentId ?? null,
  })
}
