import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import type { LabelResolver } from '../../../hooks/useResolvedLabel'
import { PICKER_PAGE_SIZE } from '../../customers/api/customerOptions'
import { getEmployee, searchEmployees } from './employeesApi'
import type { EmployeeListItem } from '../types/employee'

export interface EmployeeOptionFilters {
  isActive?: boolean
  /** Only employees WITHOUT a driver profile (the "new driver" picker). */
  excludeDrivers?: boolean
  hasDriverProfile?: boolean
}

export function employeeOptionLabel(employee: Pick<EmployeeListItem, 'firstName' | 'lastName'>): string {
  return `${employee.firstName} ${employee.lastName}`.trim()
}

export function toEmployeeOption(employee: EmployeeListItem): SearchableSelectOption {
  const label = employeeOptionLabel(employee)
  return {
    value: employee.id,
    label,
    title: label,
    subtitle: [employee.employeeNumber, employee.departmentName, employee.functionNames.join(', ')].filter(Boolean).join(' · '),
    keywords: employee.employeeNumber,
  }
}

/** PERF-H3: one page of employees matching `query` (name in any order or personnel number). */
export async function searchEmployeeOptions(
  query: string,
  signal: AbortSignal,
  filters: EmployeeOptionFilters = {},
): Promise<SearchableSelectOption[]> {
  const page = await searchEmployees(
    {
      search: query.trim() || undefined,
      isActive: filters.isActive,
      excludeDrivers: filters.excludeDrivers,
      hasDriverProfile: filters.hasDriverProfile,
      page: 1,
      pageSize: PICKER_PAGE_SIZE,
    },
    signal,
  )
  return page.items.map(toEmployeeOption)
}

/** The most common picker: active employees only. */
export const searchActiveEmployeeOptions = (query: string, signal: AbortSignal): Promise<SearchableSelectOption[]> =>
  searchEmployeeOptions(query, signal, { isActive: true })

/** Label for an employee the screen only knows by id (a `?employeeId=` link, a stored reference). */
export const resolveEmployeeLabel: LabelResolver = async (id) => employeeOptionLabel(await getEmployee(id))
