import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { LocationDetail, LocationGroup, LocationInput, LocationListItem, LocationOption, LocationType } from '../types'

export interface SearchLocationsParams {
  search?: string
  type?: LocationType
  isActive?: boolean
  customerId?: string
  country?: string
  postalCode?: string
  sort?: string
  dir?: 'asc' | 'desc'
  page: number
  pageSize: number
}

export function searchLocations(params: SearchLocationsParams): Promise<PagedResult<LocationListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.type) query.set('type', params.type)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.country) query.set('country', params.country)
  if (params.postalCode) query.set('postalCode', params.postalCode)
  if (params.sort) query.set('sort', params.sort)
  if (params.dir) query.set('dir', params.dir)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<LocationListItem>>(`/api/locations?${query.toString()}`)
}

export interface SearchLocationsGroupedParams {
  search?: string
  type?: LocationType
  isActive?: boolean
  customerId?: string
  country?: string
  postalCode?: string
  /** Sort of the locations WITHIN each group; groups themselves order by customer name. */
  innerSort?: 'name' | 'code' | 'city'
  page: number
  pageSize: number
}

/** Per-customer view: pages over GROUPS (customer / unlinked bucket last), never tearing a customer across pages. */
export function searchLocationsGrouped(params: SearchLocationsGroupedParams): Promise<PagedResult<LocationGroup>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.type) query.set('type', params.type)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.country) query.set('country', params.country)
  if (params.postalCode) query.set('postalCode', params.postalCode)
  if (params.innerSort) query.set('innerSort', params.innerSort)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<LocationGroup>>(`/api/locations/grouped?${query.toString()}`)
}

export function getLocationOptions(type?: LocationType, customerId?: string): Promise<LocationOption[]> {
  const query = new URLSearchParams()
  if (type) query.set('type', type)
  if (customerId) query.set('customerId', customerId)
  const qs = query.toString()
  return apiClient.getJson<LocationOption[]>(`/api/locations/options${qs ? `?${qs}` : ''}`)
}

export function getLocation(id: string): Promise<LocationDetail> {
  return apiClient.getJson<LocationDetail>(`/api/locations/${id}`)
}

/**
 * Creates an address. A same-front-door duplicate is refused with a 409 `address_duplicate`
 * (candidates in the body — see `extractAddressDuplicateConflict` in ./addressDuplicates)
 * unless `overrideDuplicate` is true.
 */
export function createLocation(input: LocationInput): Promise<LocationDetail> {
  return apiClient.postJson<LocationDetail, LocationInput>('/api/locations', input)
}

export function updateLocation(id: string, input: LocationInput): Promise<LocationDetail> {
  return apiClient.putJson<LocationDetail, LocationInput>(`/api/locations/${id}`, input)
}

/** Server-side copy of all master data + opening intervals: new code, "(kopie)" name, cleared defaults. */
export function duplicateLocation(id: string): Promise<LocationDetail> {
  return apiClient.postJson<LocationDetail, Record<string, never>>(`/api/locations/${id}/duplicate`, {})
}

export function setLocationActive(id: string, isActive: boolean): Promise<void> {
  return apiClient.postJson<void, { isActive: boolean }>(`/api/locations/${id}/active`, { isActive })
}

export function setLocationDefaults(
  id: string,
  defaults: { isDefaultLoadingLocation: boolean; isDefaultUnloadingLocation: boolean; isDefaultBillingLocation: boolean },
): Promise<LocationDetail> {
  return apiClient.putJson<LocationDetail, typeof defaults>(`/api/locations/${id}/defaults`, defaults)
}

export function deleteLocation(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/locations/${id}`)
}
