import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { FleetDocument, FleetDocumentInput } from '../types'

export type FleetDocumentOwnerType = 'vehicle' | 'trailer'

/** Allowed attachment types, mirrored on the file input (same as qualifications). */
export const FLEET_DOCUMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png'

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

// Multipart upload and blob download fall outside the JSON apiClient; each attaches the bearer
// token manually (same idiom as the qualification/employee-document upload).
async function readError(response: Response, fallback: string): Promise<never> {
  let message = fallback
  try {
    const data = (await response.json()) as { detail?: string; message?: string }
    message = data.detail ?? data.message ?? message
  } catch {
    // keep fallback
  }
  throw new ApiError(message, response.status)
}

export async function uploadFleetDocumentFile(id: string, file: File): Promise<FleetDocument> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/fleet-documents/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readError(response, 'Uploaden is mislukt.')
  return (await response.json()) as FleetDocument
}

export async function downloadFleetDocumentFile(id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/fleet-documents/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new ApiError('Bestand kon niet worden gedownload.', response.status)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export function deleteFleetDocumentFile(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/fleet-documents/${id}/document`)
}
