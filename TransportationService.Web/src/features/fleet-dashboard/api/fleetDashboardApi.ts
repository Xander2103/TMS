import { apiClient } from '../../../api/apiClient'
import type { FleetDashboard } from '../types'

export function getFleetDashboard(): Promise<FleetDashboard> {
  return apiClient.getJson<FleetDashboard>('/api/fleet/dashboard')
}
