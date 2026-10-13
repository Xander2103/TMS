import { apiClient } from '../../../api/apiClient'
import type { EffectivePolicies, FleetAssetKind, MaintenancePolicy, MaintenancePolicyInput } from '../types'

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

/** Effective maintenance + inspection rule of one asset, with source labels. */
export function getEffectivePolicies(assetKind: FleetAssetKind, assetId: string): Promise<EffectivePolicies> {
  return apiClient.getJson<EffectivePolicies>(
    `/api/maintenance-policies/effective?assetKind=${assetKind}&assetId=${assetId}`,
  )
}
