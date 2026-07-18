import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { TrailerDetail, TrailerInput, TrailerListItem, TrailerOperationalStatus, TrailerOption } from '../types'

export interface SearchTrailersParams {
  search?: string
  status?: TrailerOperationalStatus
  isActive?: boolean
  categoryId?: string
  page: number
  pageSize: number
}

export function searchTrailers(params: SearchTrailersParams): Promise<PagedResult<TrailerListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
  if (params.categoryId) query.set('categoryId', params.categoryId)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<TrailerListItem>>(`/api/trailers?${query.toString()}`)
}

export function getTrailerOptions(): Promise<TrailerOption[]> {
  return apiClient.getJson<TrailerOption[]>('/api/trailers/options')
}

export function getTrailer(id: string): Promise<TrailerDetail> {
  return apiClient.getJson<TrailerDetail>(`/api/trailers/${id}`)
}

export function createTrailer(input: TrailerInput): Promise<TrailerDetail> {
  return apiClient.postJson<TrailerDetail, TrailerInput>('/api/trailers', input)
}

export function updateTrailer(id: string, input: TrailerInput): Promise<TrailerDetail> {
  return apiClient.putJson<TrailerDetail, TrailerInput>(`/api/trailers/${id}`, input)
}

export function deleteTrailer(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/trailers/${id}`)
}
