import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type {
  CreateEmployeeQualificationInput,
  EmployeeQualification,
  ExpiringQualification,
  QualificationType,
  UpdateEmployeeQualificationInput,
} from '../types/qualification'

export function getEmployeeQualifications(employeeId: string): Promise<EmployeeQualification[]> {
  return apiClient.getJson<EmployeeQualification[]>(`/api/employees/${employeeId}/qualifications`)
}

export function createEmployeeQualification(
  employeeId: string,
  input: CreateEmployeeQualificationInput,
): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, CreateEmployeeQualificationInput>(
    `/api/employees/${employeeId}/qualifications`,
    input,
  )
}

export function updateEmployeeQualification(
  employeeId: string,
  id: string,
  input: UpdateEmployeeQualificationInput,
): Promise<EmployeeQualification> {
  return apiClient.putJson<EmployeeQualification, UpdateEmployeeQualificationInput>(
    `/api/employees/${employeeId}/qualifications/${id}`,
    input,
  )
}

export function verifyQualification(employeeId: string, id: string): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, Record<string, never>>(
    `/api/employees/${employeeId}/qualifications/${id}/verify`,
    {},
  )
}

export function suspendQualification(employeeId: string, id: string): Promise<EmployeeQualification> {
  return apiClient.postJson<EmployeeQualification, Record<string, never>>(
    `/api/employees/${employeeId}/qualifications/${id}/suspend`,
    {},
  )
}

export function getQualificationTypes(): Promise<QualificationType[]> {
  return apiClient.getJson<QualificationType[]>('/api/qualification-types')
}

export function getExpiringQualifications(days: number): Promise<ExpiringQualification[]> {
  return apiClient.getJson<ExpiringQualification[]>(`/api/qualifications/expiring?days=${days}`)
}

export function getExpiredQualifications(): Promise<ExpiringQualification[]> {
  return apiClient.getJson<ExpiringQualification[]>('/api/qualifications/expired')
}

// Multipart upload and blob download fall outside the JSON apiClient; both attach the
// bearer token manually.
export async function uploadQualificationDocument(
  employeeId: string,
  id: string,
  file: File,
): Promise<EmployeeQualification> {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/qualifications/${id}/document`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) {
    let message = 'Uploaden is mislukt.'
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  return (await response.json()) as EmployeeQualification
}

export async function downloadQualificationDocument(employeeId: string, id: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/employees/${employeeId}/qualifications/${id}/document`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('Document kon niet worden gedownload.', response.status)
  }
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const fileNameMatch = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileNameMatch?.[1] ?? 'document'
  anchor.click()
  URL.revokeObjectURL(url)
}

export function deleteQualificationDocument(employeeId: string, id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/employees/${employeeId}/qualifications/${id}/document`)
}
