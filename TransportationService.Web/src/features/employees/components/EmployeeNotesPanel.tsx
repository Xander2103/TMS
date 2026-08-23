import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { useEmployeeNotes } from '../hooks/useEmployeeNotes'
import { useEmployeeNoteMutations } from '../hooks/useEmployeeNoteMutations'
import type { EmployeeNote } from '../api/employeeNotesApi'
import { formatDateTime } from '../../../utils/dates'
import './EmployeeNotesPanel.css'

const MAX_NOTE_LENGTH = 4000

interface EmployeeNotesPanelProps {
  employeeId: string
}

/**
 * Corrections wave §4: multiple free-text notes per employee, replacing the legacy single
 * Employee.Notes textarea. Self-saving — every action (add/edit/delete/pin) calls its own
 * endpoint directly, so this panel needs no shared form Save button.
 */
export function EmployeeNotesPanel({ employeeId }: EmployeeNotesPanelProps) {
  const { hasPermission } = useAuth()
  const toast = useToast()
  const { t } = useLocale()
  const { notes, isLoading, error, reload } = useEmployeeNotes(employeeId)
  const mutations = useEmployeeNoteMutations()

  const canView = hasPermission('employee_notes.view')
  const canManage = hasPermission('employee_notes.manage')
  const canPin = hasPermission('employee_notes.pin')

  const [newText, setNewText] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingText, setEditingText] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<EmployeeNote | null>(null)

  if (!canView) {
    return null
  }

  if (isLoading) return <LoadingState message={t('employees.notes.loading')} />
  if (error) return <p className="placeholder-text">{error}</p>

  const sorted = [...notes].sort((a, b) => b.createdAt.localeCompare(a.createdAt))

  async function handleAdd() {
    const text = newText.trim()
    if (!text) return
    const created = await mutations.create(employeeId, text)
    if (created) {
      setNewText('')
      toast.showSuccess(t('employees.notes.added'))
      reload()
    }
  }

  function startEdit(note: EmployeeNote) {
    setEditingId(note.id)
    setEditingText(note.text)
  }

  async function saveEdit(note: EmployeeNote) {
    const text = editingText.trim()
    if (!text) return
    const updated = await mutations.update(employeeId, note.id, text)
    if (updated) {
      setEditingId(null)
      toast.showSuccess(t('employees.notes.updated'))
      reload()
    }
  }

  async function togglePin(note: EmployeeNote) {
    const updated = await mutations.setPinned(employeeId, note.id, !note.isPinnedToDashboard)
    if (updated) {
      toast.showSuccess(updated.isPinnedToDashboard ? t('employees.notes.pinnedToast') : t('employees.notes.unpinnedToast'))
      reload()
    }
  }

  return (
    <div className="employee-notes-panel">
      {sorted.length === 0 && <p className="placeholder-text">{t('employees.notes.empty')}</p>}

      {sorted.length > 0 && (
        <ul className="employee-notes-list">
          {sorted.map((note) => (
            <li key={note.id} className="employee-note-card">
              {editingId === note.id ? (
                <div className="employee-note-edit">
                  <textarea
                    value={editingText}
                    onChange={(e) => setEditingText(e.target.value)}
                    rows={3}
                    maxLength={MAX_NOTE_LENGTH}
                    aria-label={t('employees.notes.editAria')}
                  />
                  <div className="employee-note-actions">
                    <Button variant="secondary" onClick={() => setEditingId(null)} disabled={mutations.isSubmitting}>
                      {t('employees.notes.cancel')}
                    </Button>
                    <Button onClick={() => saveEdit(note)} disabled={mutations.isSubmitting}>
                      {t('employees.notes.save')}
                    </Button>
                  </div>
                </div>
              ) : (
                <>
                  <p className="employee-note-text">{note.text}</p>
                  <div className="employee-note-meta">
                    <span className="employee-note-when">
                      {formatDateTime(note.createdAt)}
                    </span>
                    {note.isPinnedToDashboard && <Badge tone="warning">{t('employees.notes.pinnedBadge')}</Badge>}
                  </div>
                  <div className="employee-note-actions">
                    {canManage && (
                      <Button variant="ghost" onClick={() => startEdit(note)} disabled={mutations.isSubmitting}>
                        {t('employees.notes.edit')}
                      </Button>
                    )}
                    {canPin && (
                      <Button variant="ghost" onClick={() => togglePin(note)} disabled={mutations.isSubmitting}>
                        {note.isPinnedToDashboard ? t('employees.notes.unpin') : t('employees.notes.pin')}
                      </Button>
                    )}
                    {canManage && (
                      <Button variant="ghost" onClick={() => setDeleteTarget(note)} disabled={mutations.isSubmitting}>
                        {t('employees.notes.remove')}
                      </Button>
                    )}
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {canManage && (
        <div className="employee-note-add">
          <textarea
            value={newText}
            onChange={(e) => setNewText(e.target.value)}
            rows={3}
            maxLength={MAX_NOTE_LENGTH}
            placeholder={t('employees.notes.newPlaceholder')}
            aria-label={t('employees.notes.newAria')}
          />
          <Button onClick={handleAdd} disabled={mutations.isSubmitting || !newText.trim()}>
            {t('employees.notes.save')}
          </Button>
        </div>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('employees.notes.deleteTitle')}
          message={t('employees.notes.deleteMessage')}
          confirmLabel={t('employees.notes.remove')}
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.remove(employeeId, deleteTarget.id)
            if (ok) {
              toast.showSuccess(t('employees.notes.deleted'))
              setDeleteTarget(null)
              reload()
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
