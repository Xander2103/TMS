import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { VehicleDetail, VehicleInput, VehicleListItem, VehicleOperationalStatus, VehicleOption } from '../types'

export interface SearchVehiclesParams {
  search?: string
  status?: VehicleOperationalStatus
  isActive?: boolean
  categoryId?: string
  page: number
  pageSize: number
}

export function searchVehicles(params: SearchVehiclesParams): Promise<PagedResult<VehicleListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.categoryId) query.set('categoryId', params.categoryId)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<VehicleListItem>>(`/api/vehicles?${query.toString()}`)
}

export function getVehicleOptions(): Promise<VehicleOption[]> {
  return apiClient.getJson<VehicleOption[]>('/api/vehicles/options')
}

export function getVehicle(id: string): Promise<VehicleDetail> {
  return apiClient.getJson<VehicleDetail>(`/api/vehicles/${id}`)
}

export function createVehicle(input: VehicleInput): Promise<VehicleDetail> {
  return apiClient.postJson<VehicleDetail, VehicleInput>('/api/vehicles', input)
}

export function updateVehicle(id: string, input: VehicleInput): Promise<VehicleDetail> {
  return apiClient.putJson<VehicleDetail, VehicleInput>(`/api/vehicles/${id}`, input)
}

export function deleteVehicle(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/vehicles/${id}`)
}
