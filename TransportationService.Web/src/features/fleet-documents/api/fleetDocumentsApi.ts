import { apiClient } from '../../../api/apiClient'
import type { FleetDocument, FleetDocumentInput } from '../types'

export type FleetDocumentOwnerType = 'vehicle' | 'trailer'

function ownerBase(ownerType: FleetDocumentOwnerType, ownerId: string): string {
  return ownerType === 'vehicle' ? `/api/vehicles/${ownerId}/documents` : `/api/trailers/${ownerId}/documents`
}

export function listFleetDocuments(ownerType: FleetDocumentOwnerType, ownerId: string): Promise<FleetDocument[]> {
  return apiClient.getJson<FleetDocument[]>(ownerBase(ownerType, ownerId))
}

export function createFleetDocument(
  ownerType: FleetDocumentOwnerType,
  ownerId: string,
  input: FleetDocumentInput,
): Promise<FleetDocument> {
  return apiClient.postJson<FleetDocument, FleetDocumentInput>(ownerBase(ownerType, ownerId), input)
}

export function updateFleetDocument(id: string, input: FleetDocumentInput): Promise<FleetDocument> {
  return apiClient.putJson<FleetDocument, FleetDocumentInput>(`/api/fleet-documents/${id}`, input)
}

export function deleteFleetDocument(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/fleet-documents/${id}`)
}
