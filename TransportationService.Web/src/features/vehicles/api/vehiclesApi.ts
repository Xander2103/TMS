import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { CreateVehicleInput, VehicleDetail, VehicleInput, VehicleListItem, VehicleOperationalStatus, VehicleOption } from '../types'

export interface SearchVehiclesParams {
  search?: string
  status?: VehicleOperationalStatus
  isActive?: boolean
  categoryId?: string
  page: number
  pageSize: number
  /** B26: whitelisted server sort key and direction ('' = the list's natural order). */
  sort?: string
  dir?: 'asc' | 'desc' | ''
}

export function searchVehicles(params: SearchVehiclesParams): Promise<PagedResult<VehicleListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.categoryId) query.set('categoryId', params.categoryId)
  if (params.sort) query.set('sort', params.sort)
  if (params.dir) query.set('dir', params.dir)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<VehicleListItem>>(`/api/vehicles?${query.toString()}`)
}

/** FL-H1/H2: `date` (YYYY-MM-DD, e.g. the trip date) evaluates each option's readiness for that day; default today. */
export function getVehicleOptions(date?: string | null): Promise<VehicleOption[]> {
  return apiClient.getJson<VehicleOption[]>(`/api/vehicles/options${date ? `?date=${date}` : ''}`)
}

export function getVehicle(id: string, signal?: AbortSignal): Promise<VehicleDetail> {
  return apiClient.getJson<VehicleDetail>(`/api/vehicles/${id}`, signal ? { signal } : undefined)
}

export function createVehicle(input: CreateVehicleInput): Promise<VehicleDetail> {
  return apiClient.postJson<VehicleDetail, CreateVehicleInput>('/api/vehicles', input)
}

export function updateVehicle(id: string, input: VehicleInput): Promise<VehicleDetail> {
  return apiClient.putJson<VehicleDetail, VehicleInput>(`/api/vehicles/${id}`, input)
}

export function setVehicleActive(id: string, isActive: boolean): Promise<void> {
  return apiClient.postJson<void, { isActive: boolean }>(`/api/vehicles/${id}/active`, { isActive })
}

export function deleteVehicle(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/vehicles/${id}`)
}

/** Assignment endpoints: the single write path for the driver↔vehicle relationship. */
export function setVehicleDriver(
  id: string,
  slot: 'fixed-driver' | 'current-driver',
  driverId: string | null,
  replaceExisting: boolean,
): Promise<VehicleDetail> {
  return apiClient.putJson<VehicleDetail, { driverId: string | null; replaceExisting: boolean }>(
    `/api/vehicles/${id}/assignments/${slot}`,
    { driverId, replaceExisting },
  )
}
