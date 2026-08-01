import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { createTasks } from '../api/tasksApi'
import { TASK_PRIORITY_LABELS, type TaskPriority } from '../api/types'
import { EmployeeMultiSelect } from './EmployeePicker'
import './tasks.css'

interface NewTaskDialogProps {
  onClose: () => void
  onCreated: () => void
}

/**
 * Create-task modal. With tasks.assign the creator picks one or more employees;
 * without it the task is always assigned to the signed-in user's own employee.
 */
export function NewTaskDialog({ onClose, onCreated }: NewTaskDialogProps) {
  const { user, hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canAssign = hasPermission('tasks.assign')
  const { options: categories } = useLookupOptions('/api/task-categories')

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [employeeIds, setEmployeeIds] = useState<string[]>([])
  const [categoryId, setCategoryId] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Normal')
  const [startAt, setStartAt] = useState('')
  const [dueAt, setDueAt] = useState('')
  const [requiresReview, setRequiresReview] = useState(false)
  const [requiresCompletionNote, setRequiresCompletionNote] = useState(false)
  const [requiresEvidence, setRequiresEvidence] = useState(false)
  const [busy, setBusy] = useState(false)
  const [titleError, setTitleError] = useState<string | undefined>()
  const [employeeError, setEmployeeError] = useState<string | undefined>()

  async function submit() {
    let valid = true
    if (title.trim().length === 0) {
      setTitleError('Een titel is verplicht.')
      valid = false
    }
    const assignedEmployeeIds = canAssign ? employeeIds : user?.employeeId ? [user.employeeId] : []
    if (assignedEmployeeIds.length === 0) {
      setEmployeeError(
        canAssign ? 'Kies minstens één medewerker.' : 'Je account is niet gekoppeld aan een medewerker.',
      )
      valid = false
    }
    if (!valid) return

    setBusy(true)
    try {
      const created = await createTasks({
        title: title.trim(),
        assignedEmployeeIds,
        description: description.trim() || undefined,
        categoryId: categoryId || undefined,
        priority,
        startAt: startAt || undefined,
        dueAt: dueAt || undefined,
        requiresReview,
        requiresCompletionNote,
        requiresEvidence,
      })
      showSuccess(created.length === 1 ? 'Taak aangemaakt.' : `${created.length} taken aangemaakt.`)
      onCreated()
    } catch (err) {
      showError(describeApiError(err, 'De taak kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Nieuwe taak"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Bezig...' : 'Taak aanmaken'}
          </Button>
        </>
      }
    >
      <FormField label="Titel" htmlFor="task-title" required error={titleError}>
        <input
          id="task-title"
          type="text"
          value={title}
          onChange={(event) => {
            setTitle(event.target.value)
            if (titleError && event.target.value.trim()) setTitleError(undefined)
          }}
          disabled={busy}
        />
      </FormField>

      <FormField label="Beschrijving" htmlFor="task-description">
        <textarea
          id="task-description"
          rows={3}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          disabled={busy}
        />
      </FormField>

      {canAssign ? (
        <FormField label="Medewerker(s)" required error={employeeError}>
          <EmployeeMultiSelect
            value={employeeIds}
            onChange={(next) => {
              setEmployeeIds(next)
              if (employeeError && next.length > 0) setEmployeeError(undefined)
            }}
            disabled={busy}
          />
        </FormField>
      ) : (
        <FormField label="Toegewezen aan" error={employeeError}>
          <p className="placeholder-text">Mijzelf</p>
        </FormField>
      )}

      <div className="task-form-row">
        <FormField label="Categorie" htmlFor="task-category">
          <select
            id="task-category"
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
            disabled={busy}
          >
            <option value="">— Geen categorie —</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label="Prioriteit" htmlFor="task-priority">
          <select
            id="task-priority"
            value={priority}
            onChange={(event) => setPriority(event.target.value as TaskPriority)}
            disabled={busy}
          >
            {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FormField>
      </div>

      <div className="task-form-row">
        <FormField label="Start" htmlFor="task-start">
          <input
            id="task-start"
            type="datetime-local"
            value={startAt}
            onChange={(event) => setStartAt(event.target.value)}
            disabled={busy}
          />
        </FormField>

        <FormField label="Deadline" htmlFor="task-due">
          <input
            id="task-due"
            type="datetime-local"
            value={dueAt}
            onChange={(event) => setDueAt(event.target.value)}
            disabled={busy}
          />
        </FormField>
      </div>

      <div className="task-template-checks">
        <label className="task-check-label">
          <input
            type="checkbox"
            checked={requiresReview}
            onChange={(event) => setRequiresReview(event.target.checked)}
            disabled={busy}
          />
          Controle vereist
        </label>
        <label className="task-check-label">
          <input
            type="checkbox"
            checked={requiresCompletionNote}
            onChange={(event) => setRequiresCompletionNote(event.target.checked)}
            disabled={busy}
          />
          Notitie bij voltooien verplicht
        </label>
        <label className="task-check-label">
          <input
            type="checkbox"
            checked={requiresEvidence}
            onChange={(event) => setRequiresEvidence(event.target.checked)}
            disabled={busy}
          />
          Bewijs (bijlage) vereist
        </label>
      </div>
    </Modal>
  )
}
