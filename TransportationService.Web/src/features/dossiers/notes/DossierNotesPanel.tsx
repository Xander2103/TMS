import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { SelfSavingPanel } from '../../../components/ui/SelfSavingPanel'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'
import { MAX_DOSSIER_NOTE_LENGTH, type DossierNote } from '../api/dossierNotesApi'
import { useDossierNotes } from './useDossierNotes'
import './dossier-notes.css'

/** Notes shown before "Alle notities tonen". */
const DEFAULT_VISIBLE_NOTES = 3
/** The character counter appears once this share of the limit is used. */
const COUNTER_THRESHOLD = Math.floor(MAX_DOSSIER_NOTE_LENGTH * 0.9)
/** Without layout (tests, hidden tab) a note counts as "long" past two lines' worth of text. */
const LONG_NOTE_CHARACTERS = 160

function looksLong(text: string): boolean {
  return text.length > LONG_NOTE_CHARACTERS || text.split('\n').length > 2
}

export interface DossierNotesPanelProps {
  dossierId: string
  /** null/omitted = dossier-level notes only; an id = only that activity's notes. */
  activityId?: string | null
  /** dossiers.manage on an open dossier; per-note rights additionally come from the DTO. */
  canWrite: boolean
  title?: string
  /** Tighter spacing for drawers and cards. */
  compact?: boolean
  /** Legacy free-text notes: shown read-only ONLY when the notes could not be loaded. */
  legacyText?: string | null
  /** Reports unsaved note text, so a hosting drawer can guard its close. */
  onDirtyChange?: (dirty: boolean) => void
  /** After every successful add/edit/delete (e.g. to refresh note counts elsewhere). */
  onChanged?: () => void
}

/**
 * User notes of a dossier or of one activity (spec §18/§19). Self-saving: add, edit and delete
 * each call their own endpoint and update the list in place. Notes are user content and stay
 * separate from the automatic audit trail, which `AuditHistoryPanel` renders.
 *
 * Keyed on its scope: another dossier/activity starts from a clean panel (no half-typed note, no
 * expanded list of the previous scope) on top of the hook's own stale-response guard.
 */
export function DossierNotesPanel(props: DossierNotesPanelProps) {
  return <ScopedNotesPanel key={`${props.dossierId}|${props.activityId ?? ''}`} {...props} />
}

function ScopedNotesPanel({
  dossierId,
  activityId = null,
  canWrite,
  title,
  compact = false,
  legacyText = null,
  onDirtyChange,
  onChanged,
}: DossierNotesPanelProps) {
  const { t } = useLocale()
  const toast = useToast()
  const { notes, isLoading, loadFailed, loadError, reload, create, update, remove } = useDossierNotes(dossierId, activityId)
  const baseId = useId()
  const listId = `${baseId}-list`
  const composerId = `${baseId}-composer`
  const addButtonRef = useRef<HTMLButtonElement>(null)

  const [composerOpen, setComposerOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [showAll, setShowAll] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<DossierNote | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [composerDirty, setComposerDirty] = useState(false)
  const [editDirty, setEditDirty] = useState(false)

  const dirty = (composerOpen && composerDirty) || (editingId !== null && editDirty)
  useEffect(() => {
    onDirtyChange?.(dirty)
    // The callback identity is the host's business; only the flag drives this effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dirty])

  const heading = title ?? t('dossierNotes.title')
  const visibleNotes = showAll ? notes : notes.slice(0, DEFAULT_VISIBLE_NOTES)
  const hiddenCount = notes.length - DEFAULT_VISIBLE_NOTES

  function closeComposer() {
    setComposerOpen(false)
    setComposerDirty(false)
    addButtonRef.current?.focus()
  }

  async function addNote(text: string): Promise<string | null> {
    try {
      await create(text)
    } catch (error) {
      return describeApiError(error, t('dossierNotes.saveFailed')).message
    }
    closeComposer()
    toast.showSuccess(t('dossierNotes.added'))
    onChanged?.()
    return null
  }

  async function saveEdit(note: DossierNote, text: string): Promise<string | null> {
    try {
      await update(note.id, text)
    } catch (error) {
      return describeApiError(error, t('dossierNotes.saveFailed')).message
    }
    setEditingId(null)
    setEditDirty(false)
    toast.showSuccess(t('dossierNotes.updated'))
    onChanged?.()
    return null
  }

  async function confirmDelete() {
    if (!deleteTarget) return
    setDeleting(true)
    setDeleteError(null)
    try {
      await remove(deleteTarget.id)
      toast.showSuccess(t('dossierNotes.deleted'))
      onChanged?.()
    } catch (error) {
      setDeleteError(describeApiError(error, t('dossierNotes.deleteFailed')).message)
    } finally {
      setDeleting(false)
      setDeleteTarget(null)
    }
  }

  return (
    <SelfSavingPanel className={compact ? 'dossier-notes dossier-notes-compact' : 'dossier-notes'}>
      <div className="dossier-notes-header">
        <h3>{heading}</h3>
        {canWrite && !isLoading && !loadFailed && (
          <Button
            ref={addButtonRef}
            variant="secondary"
            className="dossier-notes-add"
            aria-expanded={composerOpen}
            aria-controls={composerId}
            onClick={() => setComposerOpen(true)}
          >
            {t('dossierNotes.add')}
          </Button>
        )}
      </div>

      {isLoading && <LoadingState message={t('dossierNotes.loading')} />}

      {loadFailed && (
        <div className="dossier-notes-load-error">
          <p className="dossier-notes-error" role="alert">
            {describeApiError(loadError, t('dossierNotes.loadFailed')).message}
          </p>
          <Button variant="secondary" onClick={reload}>
            {t('dossierNotes.retry')}
          </Button>
          {legacyText && (
            // Fallback only: the server copies this legacy text into a first note, so it is shown
            // solely while the notes themselves are unreachable — nothing is ever hidden.
            <div className="dossier-notes-legacy">
              <h4>{t('dossierNotes.legacyTitle')}</h4>
              <p>{legacyText}</p>
            </div>
          )}
        </div>
      )}

      {/* The wrapper always exists so the add button's aria-controls resolves; the editor only while open. */}
      <div id={composerId}>
        {composerOpen && canWrite && (
          <NoteEditor
            label={t('dossierNotes.newLabel')}
            initialText=""
            autoFocus
            onSave={addNote}
            onCancel={closeComposer}
            onDirtyChange={setComposerDirty}
          />
        )}
      </div>

      {deleteError && (
        <p className="dossier-notes-error" role="alert">
          {deleteError}
        </p>
      )}

      {!isLoading && !loadFailed && notes.length === 0 && <p className="placeholder-text dossier-notes-empty">{t('dossierNotes.empty')}</p>}

      {notes.length > 0 && (
        <ul id={listId} className="dossier-notes-list">
          {visibleNotes.map((note) => (
            <li key={note.id} className="dossier-note">
              {editingId === note.id ? (
                <NoteEditor
                  label={t('dossierNotes.editLabel')}
                  initialText={note.text}
                  autoFocus
                  onSave={(text) => saveEdit(note, text)}
                  onCancel={() => {
                    setEditingId(null)
                    setEditDirty(false)
                  }}
                  onDirtyChange={setEditDirty}
                />
              ) : (
                <NoteItem
                  note={note}
                  canEdit={canWrite && note.canEdit}
                  canDelete={canWrite && note.canDelete}
                  onEdit={() => {
                    setEditDirty(false)
                    setEditingId(note.id)
                  }}
                  onDelete={() => {
                    setDeleteError(null)
                    setDeleteTarget(note)
                  }}
                />
              )}
            </li>
          ))}
        </ul>
      )}

      {hiddenCount > 0 && (
        <button
          type="button"
          className="dossier-notes-toggle"
          aria-expanded={showAll}
          aria-controls={listId}
          onClick={() => setShowAll((value) => !value)}
        >
          {showAll ? t('dossierNotes.showLess') : t('dossierNotes.showAll', { total: notes.length })}
        </button>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('dossierNotes.deleteTitle')}
          message={t('dossierNotes.deleteMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={deleting}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </SelfSavingPanel>
  )
}

interface NoteItemProps {
  note: DossierNote
  canEdit: boolean
  canDelete: boolean
  onEdit: () => void
  onDelete: () => void
}

/** One note: author + moment, the text clamped to two lines, and the per-note actions. */
function NoteItem({ note, canEdit, canDelete, onEdit, onDelete }: NoteItemProps) {
  const { t } = useLocale()
  const textId = useId()
  const [expanded, setExpanded] = useState(false)
  const [isLong, setIsLong] = useState(() => looksLong(note.text))

  // Measured against the real two-line clamp; without layout (clientHeight 0) the text length
  // decides. Skipped while expanded: an unclamped paragraph never overflows.
  const measure = useCallback(
    (element: HTMLParagraphElement | null) => {
      if (!element || expanded) return
      setIsLong(element.clientHeight > 0 ? element.scrollHeight > element.clientHeight + 1 : looksLong(note.text))
    },
    [expanded, note.text],
  )

  const edited = note.updatedAt !== note.createdAt

  return (
    <>
      <div className="dossier-note-meta">
        <span className="dossier-note-author">{note.authorName ?? t('dossierNotes.unknownAuthor')}</span>
        <time dateTime={note.createdAt}>{formatDateTime(note.createdAt)}</time>
        {edited && <span className="dossier-note-edited">{t('dossierNotes.edited', { date: formatDateTime(note.updatedAt) })}</span>}
      </div>
      <p id={textId} ref={measure} className={expanded ? 'dossier-note-text' : 'dossier-note-text dossier-note-text-clamped'}>
        {note.text}
      </p>
      {(isLong || canEdit || canDelete) && (
        <div className="dossier-note-actions">
          {isLong && (
            <button
              type="button"
              className="dossier-notes-toggle"
              aria-expanded={expanded}
              aria-controls={textId}
              onClick={() => setExpanded((value) => !value)}
            >
              {expanded ? t('dossierNotes.less') : t('dossierNotes.more')}
            </button>
          )}
          {canEdit && (
            <Button variant="ghost" onClick={onEdit}>
              {t('ui.actions.edit')}
            </Button>
          )}
          {canDelete && (
            <Button variant="ghost" onClick={onDelete}>
              {t('ui.actions.delete')}
            </Button>
          )}
        </div>
      )}
    </>
  )
}

interface NoteEditorProps {
  label: string
  initialText: string
  autoFocus?: boolean
  /** Resolves to null on success, or to the error message to show — the typed text stays. */
  onSave: (text: string) => Promise<string | null>
  onCancel: () => void
  onDirtyChange: (dirty: boolean) => void
}

/** Textarea + Opslaan/Annuleren shared by "add" and "edit"; Ctrl+Enter saves. */
function NoteEditor({ label, initialText, autoFocus = false, onSave, onCancel, onDirtyChange }: NoteEditorProps) {
  const { t } = useLocale()
  const fieldId = useId()
  const [text, setText] = useState(initialText)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const trimmed = text.trim()
  const unchanged = text === initialText
  const canSave = !saving && trimmed.length > 0 && trimmed.length <= MAX_DOSSIER_NOTE_LENGTH

  async function save() {
    if (!canSave) return
    setSaving(true)
    setError(null)
    const failure = await onSave(trimmed)
    // On success the host unmounts this editor; on failure the text stays for another attempt.
    if (failure !== null) {
      setError(failure)
      setSaving(false)
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault()
      void save()
      return
    }
    if (event.key === 'Escape') {
      // Never let Escape reach a hosting drawer: it would close it and drop the typed text.
      event.stopPropagation()
      if (unchanged && !saving) onCancel()
    }
  }

  return (
    <div className="dossier-note-editor">
      <label htmlFor={fieldId}>{label}</label>
      <textarea
        id={fieldId}
        value={text}
        rows={4}
        required
        maxLength={MAX_DOSSIER_NOTE_LENGTH}
        autoFocus={autoFocus}
        disabled={saving}
        aria-invalid={error ? true : undefined}
        aria-describedby={`${fieldId}-hint`}
        onChange={(event) => {
          setText(event.target.value)
          onDirtyChange(event.target.value !== initialText)
        }}
        onKeyDown={handleKeyDown}
      />
      <div className="dossier-note-editor-footer">
        <span id={`${fieldId}-hint`} className="dossier-note-editor-hint">
          {text.length >= COUNTER_THRESHOLD ? (
            <span className={text.length >= MAX_DOSSIER_NOTE_LENGTH ? 'dossier-note-counter dossier-note-counter-full' : 'dossier-note-counter'} aria-live="polite">
              {t('dossierNotes.counter', { used: text.length, max: MAX_DOSSIER_NOTE_LENGTH })}
            </span>
          ) : (
            t('dossierNotes.shortcutHint')
          )}
        </span>
        <div className="dossier-note-editor-actions">
          <Button variant="secondary" onClick={onCancel} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void save()} disabled={!canSave}>
            {saving ? t('ui.actions.busy') : t('ui.actions.save')}
          </Button>
        </div>
      </div>
      {error && (
        <p className="dossier-notes-error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
