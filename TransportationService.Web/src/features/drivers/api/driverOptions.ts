import type { SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import type { LabelResolver } from '../../../hooks/useResolvedLabel'
import { PICKER_PAGE_SIZE } from '../../customers/api/customerOptions'
import { getDriver, searchDrivers } from './driversApi'
import type { DriverListItem } from '../types'

export interface DriverOptionFilters {
  isActive?: boolean
  /** yyyy-MM-dd: availability (absence, employment) evaluated for that day instead of today. */
  availabilityOn?: string | null
  eligibleOnly?: boolean
}

/** PERF-H3: one page of drivers matching `query` (name in any order, driver or personnel number). */
export async function searchDriverItems(query: string, signal: AbortSignal, filters: DriverOptionFilters = {}): Promise<DriverListItem[]> {
  const page = await searchDrivers(
    {
      search: query.trim() || undefined,
      isActive: filters.isActive,
      availabilityOn: filters.availabilityOn || undefined,
      eligibleOnly: filters.eligibleOnly,
      page: 1,
      pageSize: PICKER_PAGE_SIZE,
    },
    signal,
  )
  return page.items
}

export function toDriverOption(driver: DriverListItem): SearchableSelectOption {
  return {
    value: driver.id,
    label: driver.fullName,
    title: driver.fullName,
    subtitle: [driver.driverNumber, driver.categoryName].filter(Boolean).join(' · '),
    keywords: `${driver.driverNumber} ${driver.employeeNumber}`.trim(),
  }
}

/** Async option source for `SearchableSelect onSearch`. */
export async function searchDriverOptions(query: string, signal: AbortSignal, filters: DriverOptionFilters = {}): Promise<SearchableSelectOption[]> {
  return (await searchDriverItems(query, signal, filters)).map(toDriverOption)
}

/** The most common picker: active drivers only. */
export const searchActiveDriverOptions = (query: string, signal: AbortSignal): Promise<SearchableSelectOption[]> =>
  searchDriverOptions(query, signal, { isActive: true })

/** Label for a driver the screen only knows by id (a URL filter, a stored reference). */
export const resolveDriverLabel: LabelResolver = async (id) => (await getDriver(id)).fullName
