import { useCallback, useEffect, useRef, useState } from 'react'
import {
  createDossierNote,
  deleteDossierNote,
  listDossierNotes,
  updateDossierNote,
  type DossierNote,
} from '../api/dossierNotesApi'

/**
 * The notes that belong to one scope: `activityId = null` keeps ONLY dossier-level notes, an
 * activity id keeps ONLY that activity's notes. Applied to every server answer, so a note of
 * activity 0006 can never show up under 0005 or as a dossier note — whatever the endpoint returns.
 */
export function notesInScope(notes: DossierNote[], activityId: string | null): DossierNote[] {
  return notes
    .filter((note) => (note.dossierActivityId ?? null) === activityId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

interface ScopeState {
  /** The scope this answer belongs to; an answer for another scope is treated as "still loading". */
  key: string
  notes: DossierNote[]
  /** The raw load failure (described at render time), or null when the list loaded. */
  loadError: unknown
  failed: boolean
}

export interface UseDossierNotesResult {
  notes: DossierNote[]
  isLoading: boolean
  loadFailed: boolean
  loadError: unknown
  reload: () => void
  /** Each mutation rejects with the API error and updates the list in place on success. */
  create: (text: string) => Promise<DossierNote>
  update: (noteId: string, text: string) => Promise<DossierNote>
  remove: (noteId: string) => Promise<void>
}

/**
 * Loads and mutates the notes of one scope (dossier-level or one activity). A scope change while
 * a request is in flight aborts it AND bumps the sequence, so a late answer — of a load or of a
 * mutation — never lands in the list of another dossier/activity.
 */
export function useDossierNotes(dossierId: string, activityId: string | null): UseDossierNotesResult {
  const key = `${dossierId}|${activityId ?? ''}`
  const [state, setState] = useState<ScopeState | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const seqRef = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    const seq = ++seqRef.current
    const scopeKey = `${dossierId}|${activityId ?? ''}`
    listDossierNotes(dossierId, activityId, controller.signal)
      .then((all) => {
        if (seq !== seqRef.current) return
        setState({ key: scopeKey, notes: notesInScope(all, activityId), loadError: null, failed: false })
      })
      .catch((error: unknown) => {
        if (isAbortError(error) || seq !== seqRef.current) return
        setState({ key: scopeKey, notes: [], loadError: error, failed: true })
      })
    return () => controller.abort()
  }, [dossierId, activityId, reloadToken])

  const current = state && state.key === key ? state : null

  const reload = useCallback(() => {
    setState(null)
    setReloadToken((token) => token + 1)
  }, [])

  /** Applies a mutation result only while the list on screen is still the scope it was made in. */
  const apply = useCallback(
    (change: (notes: DossierNote[]) => DossierNote[]) => {
      setState((previous) => (previous && previous.key === key && !previous.failed ? { ...previous, notes: change(previous.notes) } : previous))
    },
    [key],
  )

  const create = useCallback(
    async (text: string) => {
      const created = await createDossierNote(dossierId, text, activityId)
      // Scoped like a load: a server that answers with another scope's note never pollutes this list.
      apply((notes) => notesInScope([created, ...notes.filter((note) => note.id !== created.id)], activityId))
      return created
    },
    [dossierId, activityId, apply],
  )

  const update = useCallback(
    async (noteId: string, text: string) => {
      const updated = await updateDossierNote(dossierId, noteId, text)
      apply((notes) => notes.map((note) => (note.id === noteId ? updated : note)))
      return updated
    },
    [dossierId, apply],
  )

  const remove = useCallback(
    async (noteId: string) => {
      await deleteDossierNote(dossierId, noteId)
      apply((notes) => notes.filter((note) => note.id !== noteId))
    },
    [dossierId, apply],
  )

  return {
    notes: current?.notes ?? [],
    isLoading: current === null,
    loadFailed: current?.failed ?? false,
    loadError: current?.loadError ?? null,
    reload,
    create,
    update,
    remove,
  }
}
