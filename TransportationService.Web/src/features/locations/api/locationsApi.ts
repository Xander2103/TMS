import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { LocationDetail, LocationInput, LocationListItem, LocationOption, LocationType } from '../types'

export interface SearchLocationsParams {
  search?: string
  type?: LocationType
  isActive?: boolean
  customerId?: string
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
  if (params.sort) query.set('sort', params.sort)
  if (params.dir) query.set('dir', params.dir)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<LocationListItem>>(`/api/locations?${query.toString()}`)
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

export function createLocation(input: LocationInput): Promise<LocationDetail> {
  return apiClient.postJson<LocationDetail, LocationInput>('/api/locations', input)
}

export function updateLocation(id: string, input: LocationInput): Promise<LocationDetail> {
  return apiClient.putJson<LocationDetail, LocationInput>(`/api/locations/${id}`, input)
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
