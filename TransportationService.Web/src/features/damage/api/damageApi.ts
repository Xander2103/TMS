import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { getAccessToken } from '../../auth/authStorage'
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

// FL-M7: photo/document of the damage (pdf/jpg/png ≤ 10 MB). Multipart upload and blob download
// fall outside the JSON apiClient; each attaches the bearer token like the fleet documents do.
export const DAMAGE_ATTACHMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png'

export async function uploadDamageAttachment(id: string, file: File): Promise<DamageReport> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/damage-reports/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = translate(getActiveLocale(), 'damage.panel.uploadFailed')
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep the generic message
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as DamageReport
}

export async function downloadDamageAttachment(id: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/damage-reports/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) throw new ApiError(translate(getActiveLocale(), 'damage.panel.downloadFailed'), response.status)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export function deleteDamageAttachment(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/damage-reports/${id}/document`)
}
