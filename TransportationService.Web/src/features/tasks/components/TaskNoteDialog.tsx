import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'

interface TaskNoteDialogProps {
  title: string
  /** Label above the textarea (e.g. "Reden", "Notitie"). */
  label: string
  confirmLabel: string
  /** When true an empty note blocks submission with an inline error. */
  requireNote: boolean
  /** Optional hint under the textarea (e.g. evidence reminder). */
  hint?: string
  destructive?: boolean
  busy?: boolean
  onSubmit: (note: string) => void
  onClose: () => void
}

/** Shared note dialog for block / complete / reject actions on a task. */
export function TaskNoteDialog({
  title,
  label,
  confirmLabel,
  requireNote,
  hint,
  destructive = false,
  busy = false,
  onSubmit,
  onClose,
}: TaskNoteDialogProps) {
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | undefined>()

  function submit() {
    if (requireNote && note.trim().length === 0) {
      setError('Dit veld is verplicht.')
      return
    }
    onSubmit(note.trim())
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button variant={destructive ? 'danger' : 'primary'} onClick={submit} disabled={busy}>
            {busy ? 'Bezig...' : confirmLabel}
          </Button>
        </>
      }
    >
      <FormField label={label} htmlFor="task-note" required={requireNote} error={error} hint={hint}>
        <textarea
          id="task-note"
          rows={4}
          value={note}
          onChange={(event) => {
            setNote(event.target.value)
            if (error && event.target.value.trim().length > 0) setError(undefined)
          }}
          disabled={busy}
        />
      </FormField>
    </Modal>
  )
}
