import { apiClient } from '../../../api/apiClient'
import type { Dashboard } from '../types'

export function getDashboard(): Promise<Dashboard> {
  return apiClient.getJson<Dashboard>('/api/dashboard')
}
