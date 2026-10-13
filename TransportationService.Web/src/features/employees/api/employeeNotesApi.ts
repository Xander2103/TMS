import { apiClient } from '../../../api/apiClient'

/** Mirrors EmployeeNoteDto (camelCase JSON). */
export interface EmployeeNote {
  id: string
  employeeId: string
  text: string
  isPinnedToDashboard: boolean
  /** Set on pin, cleared on unpin — null when not currently pinned. */
  pinnedAt: string | null
  pinnedByUserId: string | null
  createdAt: string
  createdByUserId: string | null
  updatedAt: string
  updatedByUserId: string | null
}

export function listEmployeeNotes(employeeId: string): Promise<EmployeeNote[]> {
  return apiClient.getJson<EmployeeNote[]>(`/api/employees/${employeeId}/notes`)
}

export function createEmployeeNote(employeeId: string, text: string): Promise<EmployeeNote> {
  return apiClient.postJson<EmployeeNote, { text: string }>(`/api/employees/${employeeId}/notes`, { text })
}

export function updateEmployeeNote(employeeId: string, noteId: string, text: string): Promise<EmployeeNote> {
  return apiClient.putJson<EmployeeNote, { text: string }>(`/api/employees/${employeeId}/notes/${noteId}`, { text })
}

export function deleteEmployeeNote(employeeId: string, noteId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/employees/${employeeId}/notes/${noteId}`)
}

export function pinEmployeeNote(employeeId: string, noteId: string): Promise<EmployeeNote> {
  return apiClient.postJson<EmployeeNote, Record<string, never>>(`/api/employees/${employeeId}/notes/${noteId}/pin`, {})
}

export function unpinEmployeeNote(employeeId: string, noteId: string): Promise<EmployeeNote> {
  return apiClient.postJson<EmployeeNote, Record<string, never>>(`/api/employees/${employeeId}/notes/${noteId}/unpin`, {})
}
