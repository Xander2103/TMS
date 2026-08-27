import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { ActiveLegalEntity, LegalEntity, LegalEntityOption, SaveLegalEntityInput } from '../types'

export function listLegalEntities(includeInactive = false): Promise<LegalEntity[]> {
  return apiClient.getJson<LegalEntity[]>(`/api/legal-entities?includeInactive=${includeInactive}`)
}

export function getLegalEntityOptions(): Promise<LegalEntityOption[]> {
  return apiClient.getJson<LegalEntityOption[]>('/api/legal-entities/options')
}

export function getLegalEntity(id: string): Promise<LegalEntity> {
  return apiClient.getJson<LegalEntity>(`/api/legal-entities/${id}`)
}

export function createLegalEntity(input: SaveLegalEntityInput): Promise<LegalEntity> {
  return apiClient.postJson<LegalEntity, SaveLegalEntityInput>('/api/legal-entities', input)
}

export function updateLegalEntity(id: string, input: SaveLegalEntityInput): Promise<LegalEntity> {
  return apiClient.putJson<LegalEntity, SaveLegalEntityInput>(`/api/legal-entities/${id}`, input)
}

export function setLegalEntityActive(id: string, isActive: boolean): Promise<LegalEntity> {
  return apiClient.postJson<LegalEntity, { isActive: boolean }>(`/api/legal-entities/${id}/active`, { isActive })
}

// Multipart upload falls outside the JSON apiClient; the bearer token is attached manually
// (same idiom as the qualification-document upload).
export async function uploadLegalEntityLogo(id: string, file: File): Promise<LegalEntity> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/legal-entities/${id}/logo`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    // Servermessage wint; zonder servermessage (lege string) toont de aanroeper zijn eigen
    // vertaalde fallback via describeApiError.
    let message = ''
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as LegalEntity
}

export function deleteLegalEntityLogo(id: string): Promise<LegalEntity> {
  return apiClient.deleteJson<LegalEntity>(`/api/legal-entities/${id}/logo`)
}

export function getActiveLegalEntity(): Promise<ActiveLegalEntity> {
  return apiClient.getJson<ActiveLegalEntity>('/api/me/active-legal-entity')
}

export function setActiveLegalEntity(legalEntityId: string | null): Promise<ActiveLegalEntity> {
  return apiClient.putJson<ActiveLegalEntity, ActiveLegalEntity>('/api/me/active-legal-entity', { legalEntityId })
}
