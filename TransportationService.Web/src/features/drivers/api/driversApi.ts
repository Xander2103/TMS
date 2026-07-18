import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { CreateDriverInput, DriverDetail, DriverListItem, UpdateDriverInput } from '../types'

export interface SearchDriversParams {
  search?: string
  isActive?: boolean
  isBlocked?: boolean
  categoryId?: string
  page: number
  pageSize: number
}

export function searchDrivers(params: SearchDriversParams): Promise<PagedResult<DriverListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.isBlocked !== undefined) query.set('isBlocked', String(params.isBlocked))
  if (params.categoryId) query.set('categoryId', params.categoryId)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<DriverListItem>>(`/api/drivers?${query.toString()}`)
}

export function getDriver(id: string): Promise<DriverDetail> {
  return apiClient.getJson<DriverDetail>(`/api/drivers/${id}`)
}

export function createDriver(input: CreateDriverInput): Promise<DriverDetail> {
  return apiClient.postJson<DriverDetail, CreateDriverInput>('/api/drivers', input)
}

export function updateDriver(id: string, input: UpdateDriverInput): Promise<DriverDetail> {
  return apiClient.putJson<DriverDetail, UpdateDriverInput>(`/api/drivers/${id}`, input)
}

export function setDriverBlocked(id: string, isBlocked: boolean, reason: string | null): Promise<DriverDetail> {
  return apiClient.postJson<DriverDetail, { isBlocked: boolean; reason: string | null }>(
    `/api/drivers/${id}/blocked`,
    { isBlocked, reason },
  )
}

export function deleteDriver(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/drivers/${id}`)
}
