import { apiClient } from '../../../api/apiClient'
import type { CompleteInspectionInput, Inspection, InspectionInput } from '../types'

export type InspectionOwnerType = 'vehicle' | 'trailer'

export interface CompleteInspectionResponse {
  inspection: Inspection
  followUp: Inspection | null
}

function ownerBase(ownerType: InspectionOwnerType, ownerId: string): string {
  return ownerType === 'vehicle' ? `/api/vehicles/${ownerId}/inspections` : `/api/trailers/${ownerId}/inspections`
}

export function listInspections(ownerType: InspectionOwnerType, ownerId: string): Promise<Inspection[]> {
  return apiClient.getJson<Inspection[]>(ownerBase(ownerType, ownerId))
}

export function createInspection(
  ownerType: InspectionOwnerType,
  ownerId: string,
  input: InspectionInput,
): Promise<Inspection> {
  return apiClient.postJson<Inspection, InspectionInput>(ownerBase(ownerType, ownerId), input)
}

export function updateInspection(id: string, input: InspectionInput): Promise<Inspection> {
  return apiClient.putJson<Inspection, InspectionInput>(`/api/inspections/${id}`, input)
}

export function completeInspection(id: string, input: CompleteInspectionInput): Promise<CompleteInspectionResponse> {
  return apiClient.postJson<CompleteInspectionResponse, CompleteInspectionInput>(`/api/inspections/${id}/complete`, input)
}

export function deleteInspection(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/inspections/${id}`)
}
