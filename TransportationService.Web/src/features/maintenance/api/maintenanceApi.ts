import { apiClient } from '../../../api/apiClient'
import type { CompleteMaintenanceInput, MaintenanceInput, MaintenanceRecord } from '../types'

export type MaintenanceOwnerType = 'vehicle' | 'trailer'

export interface CompleteMaintenanceResponse {
  record: MaintenanceRecord
  followUp: MaintenanceRecord | null
}

function ownerBase(ownerType: MaintenanceOwnerType, ownerId: string): string {
  return ownerType === 'vehicle' ? `/api/vehicles/${ownerId}/maintenance` : `/api/trailers/${ownerId}/maintenance`
}

export function listMaintenance(ownerType: MaintenanceOwnerType, ownerId: string): Promise<MaintenanceRecord[]> {
  return apiClient.getJson<MaintenanceRecord[]>(ownerBase(ownerType, ownerId))
}

export function createMaintenance(
  ownerType: MaintenanceOwnerType,
  ownerId: string,
  input: MaintenanceInput,
): Promise<MaintenanceRecord> {
  return apiClient.postJson<MaintenanceRecord, MaintenanceInput>(ownerBase(ownerType, ownerId), input)
}

export function updateMaintenance(
  id: string,
  input: MaintenanceInput & { status: MaintenanceRecord['status'] },
): Promise<MaintenanceRecord> {
  return apiClient.putJson<MaintenanceRecord, MaintenanceInput & { status: MaintenanceRecord['status'] }>(
    `/api/maintenance/${id}`,
    input,
  )
}

export function completeMaintenance(id: string, input: CompleteMaintenanceInput): Promise<CompleteMaintenanceResponse> {
  return apiClient.postJson<CompleteMaintenanceResponse, CompleteMaintenanceInput>(`/api/maintenance/${id}/complete`, input)
}

export function deleteMaintenance(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/maintenance/${id}`)
}
