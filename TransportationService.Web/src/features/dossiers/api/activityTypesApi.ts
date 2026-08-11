import { apiClient } from '../../../api/apiClient'

export interface ActivityType {
  id: string
  code: string
  name: string
  isActive: boolean
  sortOrder: number
  icon: string | null
  kpiCategory: string | null
  hasStops: boolean
  supportsGoods: boolean
  planningRelevant: boolean
  warehouseRelevant: boolean
  allowsDuration: boolean
  isQuickStart: boolean
  quickStartOrder: number
  isSystemDefaultTransport: boolean
}

/** Create/update payload; `code` is immutable after creation (the backend refuses changes). */
export interface ActivityTypeInput {
  code: string
  name: string
  isActive: boolean
  sortOrder: number
  icon: string | null
  kpiCategory: string | null
  hasStops: boolean
  supportsGoods: boolean
  planningRelevant: boolean
  warehouseRelevant: boolean
  allowsDuration: boolean
  isQuickStart: boolean
  quickStartOrder: number
  isSystemDefaultTransport: boolean
}

export const listActivityTypes = (includeInactive = false): Promise<ActivityType[]> =>
  apiClient.getJson(`/api/activity-types${includeInactive ? '?includeInactive=true' : ''}`)

export const createActivityType = (input: ActivityTypeInput): Promise<ActivityType> =>
  apiClient.postJson('/api/activity-types', input)

export const updateActivityType = (id: string, input: ActivityTypeInput): Promise<ActivityType> =>
  apiClient.putJson(`/api/activity-types/${id}`, input)

export const removeActivityType = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/activity-types/${id}`)
