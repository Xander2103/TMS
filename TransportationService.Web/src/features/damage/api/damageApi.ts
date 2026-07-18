import { apiClient } from '../../../api/apiClient'
import type { CreateDamageInput, DamageReport, UpdateDamageInput } from '../types'

export type DamageOwnerType = 'vehicle' | 'trailer'

function ownerBase(ownerType: DamageOwnerType, ownerId: string): string {
  return ownerType === 'vehicle'
    ? `/api/vehicles/${ownerId}/damage-reports`
    : `/api/trailers/${ownerId}/damage-reports`
}

export function listDamageReports(ownerType: DamageOwnerType, ownerId: string): Promise<DamageReport[]> {
  return apiClient.getJson<DamageReport[]>(ownerBase(ownerType, ownerId))
}

export function createDamageReport(
  ownerType: DamageOwnerType,
  ownerId: string,
  input: CreateDamageInput,
): Promise<DamageReport> {
  return apiClient.postJson<DamageReport, CreateDamageInput>(ownerBase(ownerType, ownerId), input)
}

export function updateDamageReport(id: string, input: UpdateDamageInput): Promise<DamageReport> {
  return apiClient.putJson<DamageReport, UpdateDamageInput>(`/api/damage-reports/${id}`, input)
}

export function deleteDamageReport(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/damage-reports/${id}`)
}
