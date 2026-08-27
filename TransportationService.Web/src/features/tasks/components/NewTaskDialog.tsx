import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      setTitleError(t('tasks.new.titleRequired'))
      valid = false
    }
    const assignedEmployeeIds = canAssign ? employeeIds : user?.employeeId ? [user.employeeId] : []
    if (assignedEmployeeIds.length === 0) {
      setEmployeeError(canAssign ? t('tasks.new.chooseEmployees') : t('tasks.new.noEmployeeLinked'))
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
      showSuccess(t('tasks.new.created', { count: created.length }))
      onCreated()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.new.createFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('tasks.new.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('tasks.new.create')}
          </Button>
        </>
      }
    >
      <FormField label={t('tasks.new.titleLabel')} htmlFor="task-title" required error={titleError}>
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

      <FormField label={t('tasks.new.description')} htmlFor="task-description">
        <textarea
          id="task-description"
          rows={3}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          disabled={busy}
        />
      </FormField>

      {canAssign ? (
        <FormField label={t('tasks.new.employees')} required error={employeeError}>
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
        <FormField label={t('tasks.new.assignedTo')} error={employeeError}>
          <p className="placeholder-text">{t('tasks.new.myself')}</p>
        </FormField>
      )}

      <div className="task-form-row">
        <FormField label={t('tasks.new.category')} htmlFor="task-category">
          <select
            id="task-category"
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
            disabled={busy}
          >
            <option value="">{t('tasks.new.noCategory')}</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </FormField>

        <FormField label={t('tasks.new.priority')} htmlFor="task-priority">
          <select
            id="task-priority"
            value={priority}
            onChange={(event) => setPriority(event.target.value as TaskPriority)}
            disabled={busy}
          >
            {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {t(label)}
              </option>
            ))}
          </select>
        </FormField>
      </div>

      <div className="task-form-row">
        <FormField label={t('tasks.new.start')} htmlFor="task-start">
          <input
            id="task-start"
            type="datetime-local"
            value={startAt}
            onChange={(event) => setStartAt(event.target.value)}
            disabled={busy}
          />
        </FormField>

        <FormField label={t('tasks.new.due')} htmlFor="task-due">
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
          {t('tasks.new.requiresReview')}
        </label>
        <label className="task-check-label">
          <input
            type="checkbox"
            checked={requiresCompletionNote}
            onChange={(event) => setRequiresCompletionNote(event.target.checked)}
            disabled={busy}
          />
          {t('tasks.new.requiresNote')}
        </label>
        <label className="task-check-label">
          <input
            type="checkbox"
            checked={requiresEvidence}
            onChange={(event) => setRequiresEvidence(event.target.checked)}
            disabled={busy}
          />
          {t('tasks.new.requiresEvidence')}
        </label>
      </div>
    </Modal>
  )
}
