import { apiClient, ApiError } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import { getAccessToken } from '../../auth/authStorage'
import { apiBaseUrl } from '../../../config/env'
import type {
  ExceptionDetail,
  ExceptionListItem,
  ExceptionSeverity,
  ExecutionExceptionStatus,
  ExecutionExceptionType,
  ReportExceptionInput,
} from '../types'

export function reportException(tripId: string, input: ReportExceptionInput): Promise<ExceptionDetail> {
  return apiClient.postJson<ExceptionDetail, ReportExceptionInput>(`/api/trips/${tripId}/exceptions`, input)
}

export function listTripExceptions(tripId: string): Promise<ExceptionListItem[]> {
  return apiClient.getJson<ExceptionListItem[]>(`/api/trips/${tripId}/exceptions`)
}

export interface SearchExceptionsParams {
  status?: ExecutionExceptionStatus
  type?: ExecutionExceptionType
  severity?: ExceptionSeverity
  packagesOnly?: boolean
  assignedToUserId?: string
  page: number
  pageSize: number
}

export function searchExceptions(params: SearchExceptionsParams): Promise<PagedResult<ExceptionListItem>> {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.type) query.set('type', params.type)
  if (params.severity) query.set('severity', params.severity)
  if (params.packagesOnly) query.set('packagesOnly', 'true')
  if (params.assignedToUserId) query.set('assignedToUserId', params.assignedToUserId)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<ExceptionListItem>>(`/api/exceptions?${query.toString()}`)
}

export function assignException(id: string, userId: string | null): Promise<ExceptionDetail> {
  return apiClient.postJson<ExceptionDetail, { userId: string | null }>(`/api/exceptions/${id}/assign`, { userId })
}

export function getException(id: string): Promise<ExceptionDetail> {
  return apiClient.getJson<ExceptionDetail>(`/api/exceptions/${id}`)
}

export function changeExceptionStatus(
  id: string,
  status: ExecutionExceptionStatus,
  note: string | null,
): Promise<ExceptionDetail> {
  return apiClient.postJson<ExceptionDetail, { status: ExecutionExceptionStatus; note: string | null }>(
    `/api/exceptions/${id}/status`,
    { status, note },
  )
}

export function updateException(
  id: string,
  input: { severity: ExceptionSeverity; dispatcherNotes: string | null; customerVisible: boolean },
): Promise<ExceptionDetail> {
  return apiClient.putJson<ExceptionDetail, typeof input>(`/api/exceptions/${id}`, input)
}

/** Multipart upload falls outside the JSON client; the bearer token is attached manually. */
export async function uploadExceptionPhoto(id: string, file: File): Promise<ExceptionDetail> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/exceptions/${id}/photos`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  if (!response.ok) {
    throw new ApiError('De foto kon niet worden geüpload.', response.status)
  }
  return (await response.json()) as ExceptionDetail
}

/** Fetches a photo as an object URL so <img> can show it with the auth header applied. */
export async function fetchExceptionPhotoUrl(id: string, photoId: string): Promise<string> {
  const response = await fetch(`${apiBaseUrl}/api/exceptions/${id}/photos/${photoId}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('De foto kon niet worden geladen.', response.status)
  }
  return URL.createObjectURL(await response.blob())
}

export async function deleteExceptionPhoto(id: string, photoId: string): Promise<ExceptionDetail> {
  await apiClient.deleteRequest(`/api/exceptions/${id}/photos/${photoId}`)
  return getException(id)
}
