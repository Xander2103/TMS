import { apiClient } from '../../../api/apiClient'
import { periodEnd, type PlanningGroupFilters, type PlanningSelection } from '../planningFilters'
import type {
  PlanningEmployeeOption, PlanningFilterOptions, ScheduleGrid, Shift, ShiftInput, ShiftStatus,
} from '../types'

/**
 * Selection → query string of the planning endpoints (grid and export share it). Lists are sent
 * as repeated keys, which is what ASP.NET Core binds to `Guid[]`.
 */
export function scheduleQueryParams(selection: PlanningSelection): URLSearchParams {
  const query = new URLSearchParams({ from: selection.weekStart, to: periodEnd(selection.weekStart, selection.weeks) })
  for (const id of selection.departmentIds) query.append('departmentIds', id)
  for (const id of selection.jobFunctionIds) query.append('jobFunctionIds', id)
  for (const id of selection.contractTypeIds) query.append('contractTypeIds', id)
  for (const status of selection.statuses) query.append('statuses', status)
  if (selection.driversOnly) query.set('driversOnly', 'true')
  for (const id of selection.employeeIds) query.append('employeeIds', id)
  return query
}

/** Server-side filtered grid for the selection; `signal` cancels a superseded request. */
export function getScheduleForSelection(selection: PlanningSelection, signal?: AbortSignal): Promise<ScheduleGrid> {
  return apiClient.getJson<ScheduleGrid>(`/api/employee-planning?${scheduleQueryParams(selection).toString()}`, { signal })
}

/** Legacy single-filter shape (portal self-schedule and older callers). */
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

/**
 * GLOBAL employee search of the planning: all employees the caller may plan, independent of the
 * active planning filters. Used by the employee picker only.
 */
export function searchPlanningEmployees(search: string, signal?: AbortSignal, limit = 20): Promise<PlanningEmployeeOption[]> {
  const query = new URLSearchParams({ limit: String(limit) })
  if (search.trim()) query.set('search', search.trim())
  return apiClient.getJson<PlanningEmployeeOption[]>(`/api/employee-planning/employees?${query.toString()}`, { signal })
}

export function getPlanningFilterOptions(signal?: AbortSignal): Promise<PlanningFilterOptions> {
  return apiClient.getJson<PlanningFilterOptions>('/api/employee-planning/filter-options', { signal })
}

/** Downloads the xlsx for exactly the current selection (same parameters as the grid). */
export function downloadScheduleExcel(selection: PlanningSelection): Promise<void> {
  const to = periodEnd(selection.weekStart, selection.weeks)
  return apiClient.downloadFile(
    `/api/employee-planning/export?${scheduleQueryParams(selection).toString()}`,
    `personeelsplanning_${selection.weekStart}_${to}.xlsx`,
  )
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

/**
 * Copies the source week's shifts of the current SELECTION (group filters, status scope and
 * explicit picks resolve through the same backend filter as the grid). Without a selection the
 * backend copies the whole tenant, as before this wave.
 */
export function copyWeek(
  sourceWeekStart: string,
  targetWeekStart: string,
  selection?: PlanningGroupFilters & { employeeIds: string[] },
): Promise<CopyWeekResult> {
  return apiClient.postJson<CopyWeekResult, object>('/api/shifts/copy-week', {
    sourceWeekStart,
    targetWeekStart,
    employeeId: null,
    departmentId: null,
    departmentIds: selection?.departmentIds ?? [],
    jobFunctionIds: selection?.jobFunctionIds ?? [],
    contractTypeIds: selection?.contractTypeIds ?? [],
    statuses: selection?.statuses ?? [],
    driversOnly: selection?.driversOnly ?? false,
    employeeIds: selection?.employeeIds ?? [],
  })
}
