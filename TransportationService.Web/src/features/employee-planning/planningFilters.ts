import { mondayOf, toIsoDate } from '../../components/calendar/dateUtils'

/**
 * The planning SELECTION — the single state that drives the grid, the list view, "week kopiëren"
 * and the Excel export. Mirrors the backend `ScheduleQuery`:
 *
 *  - within one dimension OR, between dimensions AND;
 *  - `statuses` is the scope (default: in dienst) and never applies to explicit picks;
 *  - `employeeIds` are EXPLICIT picks from the global search. Without group filters they ARE
 *    the selection; with group filters they are pinned on top of the group result.
 *
 * The URL is the source of truth for this state (F5, Back/Forward, bookmarks, sharing); the
 * per-user "last used" copy in localStorage only seeds a URL that carries no filter at all.
 */

export type PlanningEmployeeStatus = 'Active' | 'Inactive'
export type PlanningWeeks = 1 | 2 | 4

export interface PlanningGroupFilters {
  departmentIds: string[]
  jobFunctionIds: string[]
  contractTypeIds: string[]
  statuses: PlanningEmployeeStatus[]
  driversOnly: boolean
}

export interface PlanningSelection extends PlanningGroupFilters {
  /** ISO Monday of the first displayed week. */
  weekStart: string
  weeks: PlanningWeeks
  employeeIds: string[]
  listView: boolean
}

export const EMPTY_GROUP_FILTERS: PlanningGroupFilters = {
  departmentIds: [],
  jobFunctionIds: [],
  contractTypeIds: [],
  statuses: [],
  driversOnly: false,
}

export function defaultSelection(today: Date = new Date()): PlanningSelection {
  return { ...EMPTY_GROUP_FILTERS, weekStart: toIsoDate(mondayOf(today)), weeks: 1, employeeIds: [], listView: false }
}

export function hasGroupFilters(filters: PlanningGroupFilters): boolean {
  return (
    filters.departmentIds.length > 0 ||
    filters.jobFunctionIds.length > 0 ||
    filters.contractTypeIds.length > 0 ||
    filters.driversOnly
  )
}

/** True when anything narrows the default "all active employees" view. */
export function hasAnyFilter(selection: PlanningSelection): boolean {
  return hasGroupFilters(selection) || selection.statuses.length > 0 || selection.employeeIds.length > 0
}

/** Scope actually applied by the backend when the user picked nothing. */
export function effectiveStatuses(statuses: PlanningEmployeeStatus[]): PlanningEmployeeStatus[] {
  return statuses.length === 0 ? ['Active'] : statuses
}

export function periodEnd(weekStart: string, weeks: PlanningWeeks): string {
  const start = new Date(`${weekStart}T00:00:00`)
  return toIsoDate(new Date(start.getTime() + (weeks * 7 - 1) * 86_400_000))
}

export function shiftWeekStart(weekStart: string, deltaWeeks: number): string {
  const date = new Date(`${weekStart}T00:00:00`)
  date.setDate(date.getDate() + deltaWeeks * 7)
  return toIsoDate(date)
}

// ---- URL (de)serialisation ------------------------------------------------------------------

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Query keys of the planning page. Stable ids only — never display names. */
export const URL_KEYS = {
  week: 'week',
  weeks: 'weeks',
  departments: 'departments',
  functions: 'functions',
  contracts: 'contracts',
  status: 'status',
  drivers: 'drivers',
  employees: 'employees',
  view: 'view',
} as const

const FILTER_KEYS: string[] = [
  URL_KEYS.departments, URL_KEYS.functions, URL_KEYS.contracts, URL_KEYS.status, URL_KEYS.drivers, URL_KEYS.employees,
]

function idList(raw: string | null): string[] {
  if (!raw) return []
  return [...new Set(raw.split(',').map((v) => v.trim()).filter((v) => GUID.test(v)))]
}

function statusList(raw: string | null): PlanningEmployeeStatus[] {
  if (!raw) return []
  const values = raw.split(',').map((v) => v.trim().toLowerCase())
  const result: PlanningEmployeeStatus[] = []
  if (values.includes('active')) result.push('Active')
  if (values.includes('inactive')) result.push('Inactive')
  return result
}

/** True when the URL carries any selection filter (then the stored last-used filters are ignored). */
export function urlHasFilters(params: URLSearchParams): boolean {
  return FILTER_KEYS.some((key) => params.has(key))
}

export function selectionFromSearchParams(params: URLSearchParams, today: Date = new Date()): PlanningSelection {
  const base = defaultSelection(today)
  const week = params.get(URL_KEYS.week)
  const weeksRaw = Number(params.get(URL_KEYS.weeks))
  return {
    weekStart: week && ISO_DATE.test(week) && !Number.isNaN(new Date(`${week}T00:00:00`).getTime())
      ? toIsoDate(mondayOf(new Date(`${week}T00:00:00`)))
      : base.weekStart,
    weeks: weeksRaw === 2 || weeksRaw === 4 ? weeksRaw : 1,
    departmentIds: idList(params.get(URL_KEYS.departments)),
    jobFunctionIds: idList(params.get(URL_KEYS.functions)),
    contractTypeIds: idList(params.get(URL_KEYS.contracts)),
    statuses: statusList(params.get(URL_KEYS.status)),
    driversOnly: params.get(URL_KEYS.drivers) === '1',
    employeeIds: idList(params.get(URL_KEYS.employees)),
    listView: params.get(URL_KEYS.view) === 'list',
  }
}

/** Only non-default values are written, so URLs stay short and a plain /employee-planning stays plain. */
export function selectionToSearchParams(selection: PlanningSelection, today: Date = new Date()): URLSearchParams {
  const params = new URLSearchParams()
  const base = defaultSelection(today)
  if (selection.weekStart !== base.weekStart) params.set(URL_KEYS.week, selection.weekStart)
  if (selection.weeks !== 1) params.set(URL_KEYS.weeks, String(selection.weeks))
  if (selection.departmentIds.length) params.set(URL_KEYS.departments, selection.departmentIds.join(','))
  if (selection.jobFunctionIds.length) params.set(URL_KEYS.functions, selection.jobFunctionIds.join(','))
  if (selection.contractTypeIds.length) params.set(URL_KEYS.contracts, selection.contractTypeIds.join(','))
  if (selection.statuses.length) params.set(URL_KEYS.status, selection.statuses.map((s) => s.toLowerCase()).join(','))
  if (selection.driversOnly) params.set(URL_KEYS.drivers, '1')
  if (selection.employeeIds.length) params.set(URL_KEYS.employees, selection.employeeIds.join(','))
  if (selection.listView) params.set(URL_KEYS.view, 'list')
  return params
}

// ---- last used filters per user ---------------------------------------------------------------

const STORAGE_PREFIX = 'ts.employeePlanning.filters:'

/** Group filters + status are remembered per user; the week and explicit picks are not. */
export function readLastUsedFilters(userId: string | null): PlanningGroupFilters | null {
  if (!userId) return null
  try {
    const raw = window.localStorage.getItem(`${STORAGE_PREFIX}${userId}`)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<PlanningGroupFilters>
    return {
      departmentIds: Array.isArray(parsed.departmentIds) ? parsed.departmentIds.filter((v) => GUID.test(String(v))) : [],
      jobFunctionIds: Array.isArray(parsed.jobFunctionIds) ? parsed.jobFunctionIds.filter((v) => GUID.test(String(v))) : [],
      contractTypeIds: Array.isArray(parsed.contractTypeIds) ? parsed.contractTypeIds.filter((v) => GUID.test(String(v))) : [],
      statuses: Array.isArray(parsed.statuses)
        ? parsed.statuses.filter((v): v is PlanningEmployeeStatus => v === 'Active' || v === 'Inactive')
        : [],
      driversOnly: parsed.driversOnly === true,
    }
  } catch {
    return null
  }
}

export function writeLastUsedFilters(userId: string | null, filters: PlanningGroupFilters): void {
  if (!userId) return
  try {
    const { departmentIds, jobFunctionIds, contractTypeIds, statuses, driversOnly } = filters
    window.localStorage.setItem(
      `${STORAGE_PREFIX}${userId}`,
      JSON.stringify({ departmentIds, jobFunctionIds, contractTypeIds, statuses, driversOnly }),
    )
  } catch {
    /* best-effort convenience */
  }
}
