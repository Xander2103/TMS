import { apiClient, ApiError } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
import { getAccessToken } from '../../auth/authStorage'
import type { Absence, AbsenceInput } from '../../absences/types'
import type { MyDashboard, MyProfile, MyQualification } from '../types'

export function getMyProfile(): Promise<MyProfile> {
  return apiClient.getJson<MyProfile>('/api/me/profile')
}

export function getMyDashboard(): Promise<MyDashboard> {
  return apiClient.getJson<MyDashboard>('/api/me/dashboard')
}

export function listMyAbsences(): Promise<Absence[]> {
  return apiClient.getJson<Absence[]>('/api/me/absences')
}

export function createMyAbsence(input: AbsenceInput): Promise<Absence> {
  return apiClient.postJson<Absence, AbsenceInput>('/api/me/absences', input)
}

export function cancelMyAbsence(id: string): Promise<Absence> {
  return apiClient.postJson<Absence, Record<string, never>>(`/api/me/absences/${id}/cancel`, {})
}

export function listMyQualifications(): Promise<MyQualification[]> {
  return apiClient.getJson<MyQualification[]>('/api/me/qualifications')
}

/** Own qualification document as an object URL (auth header applied). */
export async function fetchMyQualificationDocumentUrl(id: string): Promise<string> {
  const response = await fetch(`${apiBaseUrl}/api/me/qualifications/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError(translate(getActiveLocale(), 'portalHome.api.documentLoadFailed'), response.status)
  }
  return URL.createObjectURL(await response.blob())
}

export async function uploadMyAbsenceAttachment(absenceId: string, file: File): Promise<Absence> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/me/absences/${absenceId}/attachment`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  if (!response.ok) {
    throw new ApiError(translate(getActiveLocale(), 'portalHome.api.attachmentUploadFailed'), response.status)
  }
  return (await response.json()) as Absence
}

export function changeMyPassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiClient.postJson<void, { currentPassword: string; newPassword: string }>('/api/me/password', {
    currentPassword,
    newPassword,
  })
}
