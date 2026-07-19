import { apiClient } from '../../../api/apiClient'
import type { Absence, AbsenceInput, AbsenceReviewContext, AbsenceStatus, AbsenceType } from '../types'

export interface ListAbsencesParams {
  from?: string
  to?: string
  type?: AbsenceType
  status?: AbsenceStatus
}

export function listAbsences(params: ListAbsencesParams = {}): Promise<Absence[]> {
  const query = new URLSearchParams()
  if (params.from) query.set('from', params.from)
  if (params.to) query.set('to', params.to)
  if (params.type) query.set('type', params.type)
  if (params.status) query.set('status', params.status)
  const suffix = query.toString()
  return apiClient.getJson<Absence[]>(`/api/absences${suffix ? `?${suffix}` : ''}`)
}

export function listEmployeeAbsences(employeeId: string): Promise<Absence[]> {
  return apiClient.getJson<Absence[]>(`/api/employees/${employeeId}/absences`)
}

export function createAbsence(employeeId: string, input: AbsenceInput): Promise<Absence> {
  return apiClient.postJson<Absence, AbsenceInput>(`/api/employees/${employeeId}/absences`, input)
}

export function updateAbsence(id: string, input: AbsenceInput): Promise<Absence> {
  return apiClient.putJson<Absence, AbsenceInput>(`/api/absences/${id}`, input)
}

export function decideAbsence(id: string, approve: boolean, note: string | null): Promise<Absence> {
  return apiClient.postJson<Absence, { approve: boolean; note: string | null }>(`/api/absences/${id}/decision`, {
    approve,
    note,
  })
}

export function startAbsenceReview(id: string): Promise<Absence> {
  return apiClient.postJson<Absence, Record<string, never>>(`/api/absences/${id}/review`, {})
}

export function requestAbsenceChanges(
  id: string,
  note: string,
  proposedStartDate: string | null,
  proposedEndDate: string | null,
): Promise<Absence> {
  return apiClient.postJson<Absence, { note: string; proposedStartDate: string | null; proposedEndDate: string | null }>(
    `/api/absences/${id}/request-changes`,
    { note, proposedStartDate, proposedEndDate },
  )
}

export function setAbsenceInternalNote(id: string, note: string | null): Promise<Absence> {
  return apiClient.putJson<Absence, { note: string | null }>(`/api/absences/${id}/internal-note`, { note })
}

export function getAbsenceReviewContext(id: string): Promise<AbsenceReviewContext> {
  return apiClient.getJson<AbsenceReviewContext>(`/api/absences/${id}/review-context`)
}

export function cancelAbsence(id: string): Promise<Absence> {
  return apiClient.postJson<Absence, Record<string, never>>(`/api/absences/${id}/cancel`, {})
}

export function deleteAbsence(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/absences/${id}`)
}
