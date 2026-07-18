import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { LookupInput, LookupItem, LookupOption } from '../types'

export interface LookupSearchParams {
  search?: string
  isActive?: boolean
  page: number
  pageSize: number
}

/**
 * Builds a typed CRUD client for a lookup resource at `basePath` (e.g. `/api/departments`).
 * A single factory backs every lookup screen, mirroring the backend's generic controller.
 */
export function createLookupApi(basePath: string) {
  return {
    search(params: LookupSearchParams): Promise<PagedResult<LookupItem>> {
      const query = new URLSearchParams()
      if (params.search) query.set('search', params.search)
      if (params.isActive !== undefined) query.set('isActive', String(params.isActive))
      query.set('page', String(params.page))
      query.set('pageSize', String(params.pageSize))
      return apiClient.getJson<PagedResult<LookupItem>>(`${basePath}?${query.toString()}`)
    },

    options(): Promise<LookupOption[]> {
      return apiClient.getJson<LookupOption[]>(`${basePath}/options`)
    },

    get(id: string): Promise<LookupItem> {
      return apiClient.getJson<LookupItem>(`${basePath}/${id}`)
    },

    create(input: LookupInput): Promise<LookupItem> {
      return apiClient.postJson<LookupItem, LookupInput>(basePath, input)
    },

    update(id: string, input: LookupInput): Promise<LookupItem> {
      return apiClient.putJson<LookupItem, LookupInput>(`${basePath}/${id}`, input)
    },

    remove(id: string): Promise<void> {
      return apiClient.deleteRequest(`${basePath}/${id}`)
    },
  }
}

export type LookupApi = ReturnType<typeof createLookupApi>
