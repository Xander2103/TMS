import { apiClient, ApiError } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
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
    throw new ApiError('Het document kon niet worden geladen.', response.status)
  }
  return URL.createObjectURL(await response.blob())
}

export function changeMyPassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiClient.postJson<void, { currentPassword: string; newPassword: string }>('/api/me/password', {
    currentPassword,
    newPassword,
  })
}
