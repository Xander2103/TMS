import { apiClient } from '../../../api/apiClient'

/** Server-side limit of one note (contract 4.1). */
export const MAX_DOSSIER_NOTE_LENGTH = 4000

/** Mirrors DossierNoteDto (camelCase JSON, contract 4.1). */
export interface DossierNote {
  id: string
  dossierId: string
  /** null = a dossier-level note; otherwise the activity the note belongs to. */
  dossierActivityId: string | null
  text: string
  authorName: string | null
  createdAt: string
  updatedAt: string
  /** Per-note rights of the CURRENT user, decided by the server. */
  canEdit: boolean
  canDelete: boolean
}

/**
 * Notes of one dossier, newest first. Without `activityId` the server returns EVERY note of the
 * dossier (dossier-level and activity notes alike) — callers scope the result themselves.
 */
export function listDossierNotes(dossierId: string, activityId?: string | null, signal?: AbortSignal): Promise<DossierNote[]> {
  const query = activityId ? `?activityId=${encodeURIComponent(activityId)}` : ''
  return apiClient.getJson<DossierNote[]>(`/api/dossiers/${dossierId}/notes${query}`, { signal })
}

export function createDossierNote(dossierId: string, text: string, dossierActivityId?: string | null): Promise<DossierNote> {
  return apiClient.postJson<DossierNote, { text: string; dossierActivityId: string | null }>(`/api/dossiers/${dossierId}/notes`, {
    text,
    dossierActivityId: dossierActivityId ?? null,
  })
}

export function updateDossierNote(dossierId: string, noteId: string, text: string): Promise<DossierNote> {
  return apiClient.putJson<DossierNote, { text: string }>(`/api/dossiers/${dossierId}/notes/${noteId}`, { text })
}

export function deleteDossierNote(dossierId: string, noteId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/dossiers/${dossierId}/notes/${noteId}`)
}
