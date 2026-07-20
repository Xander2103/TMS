import { apiClient } from '../../../api/apiClient'
import type { MaintenancePolicy, MaintenancePolicyInput } from '../types'

export function listMaintenancePolicies(): Promise<MaintenancePolicy[]> {
  return apiClient.getJson<MaintenancePolicy[]>('/api/maintenance-policies')
}

export function createMaintenancePolicy(input: MaintenancePolicyInput): Promise<MaintenancePolicy> {
  return apiClient.postJson<MaintenancePolicy, MaintenancePolicyInput>('/api/maintenance-policies', input)
}

export function updateMaintenancePolicy(id: string, input: MaintenancePolicyInput): Promise<MaintenancePolicy> {
  return apiClient.putJson<MaintenancePolicy, MaintenancePolicyInput>(`/api/maintenance-policies/${id}`, input)
}

export function deleteMaintenancePolicy(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/maintenance-policies/${id}`)
}
