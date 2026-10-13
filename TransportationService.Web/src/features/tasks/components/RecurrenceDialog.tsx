import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import {
  TASK_RECURRENCE_INTERVAL_LABELS,
  type SaveTaskRecurrenceInput,
  type TaskRecurrence,
  type TaskRecurrenceInterval,
  type TaskTemplate,
} from '../api/types'
import { EmployeeSelect } from './EmployeePicker'
import './tasks.css'

interface RecurrenceDialogProps {
  initial: TaskRecurrence | null
  templates: TaskTemplate[]
  busy: boolean
  onSubmit: (input: SaveTaskRecurrenceInput) => void
  onClose: () => void
}

/** Create/edit a recurring schedule that materialises a template into tasks. */
export function RecurrenceDialog({ initial, templates, busy, onSubmit, onClose }: RecurrenceDialogProps) {
  const { t } = useLocale()
  const [templateId, setTemplateId] = useState(initial?.templateId ?? '')
  const [employeeId, setEmployeeId] = useState<string | null>(initial?.assignedEmployeeId ?? null)
  const [interval, setInterval] = useState<TaskRecurrenceInterval>(initial?.interval ?? 'Weekly')
  const [customIntervalDays, setCustomIntervalDays] = useState<string>(
    initial?.customIntervalDays != null ? String(initial.customIntervalDays) : '',
  )
  const [startDate, setStartDate] = useState(initial?.startDate ?? '')
  const [endDate, setEndDate] = useState(initial?.endDate ?? '')
  const [isActive, setIsActive] = useState(initial?.isActive ?? true)
  const [errors, setErrors] = useState<Record<string, string>>({})

  function submit() {
    const next: Record<string, string> = {}
    if (!templateId) next.template = t('tasks.recurrenceDialog.chooseTemplate')
    if (!employeeId) next.employee = t('tasks.recurrenceDialog.chooseEmployee')
    if (!startDate) next.startDate = t('tasks.recurrenceDialog.startDateRequired')
    if (interval === 'CustomDays' && (!customIntervalDays || Number(customIntervalDays) < 1)) {
      next.customDays = t('tasks.recurrenceDialog.invalidDays')
    }
    setErrors(next)
    if (Object.keys(next).length > 0) return

    onSubmit({
      templateId,
      assignedEmployeeId: employeeId!,
      interval,
      customIntervalDays: interval === 'CustomDays' ? Number(customIntervalDays) : null,
      startDate,
      endDate: endDate || null,
      isActive,
    })
  }

  return (
    <Modal
      title={initial ? t('tasks.recurrenceDialog.editTitle') : t('tasks.recurrenceDialog.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <FormField label={t('tasks.recurrenceDialog.template')} htmlFor="recurrence-template" required error={errors.template}>
        <select
          id="recurrence-template"
          value={templateId}
          onChange={(event) => setTemplateId(event.target.value)}
          disabled={busy}
        >
          <option value="">{t('ui.select.placeholder')}</option>
          {templates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.name}
            </option>
          ))}
        </select>
      </FormField>

      <FormField label={t('tasks.recurrenceDialog.employee')} required error={errors.employee}>
        <EmployeeSelect value={employeeId} onChange={setEmployeeId} disabled={busy} />
      </FormField>

      <div className="task-form-row">
        <FormField label={t('tasks.recurrenceDialog.interval')} htmlFor="recurrence-interval">
          <select
            id="recurrence-interval"
            value={interval}
            onChange={(event) => setInterval(event.target.value as TaskRecurrenceInterval)}
            disabled={busy}
          >
            {Object.entries(TASK_RECURRENCE_INTERVAL_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {t(label)}
              </option>
            ))}
          </select>
        </FormField>
        {interval === 'CustomDays' && (
          <FormField label={t('tasks.recurrenceDialog.customDays')} htmlFor="recurrence-custom-days" required error={errors.customDays}>
            <input
              id="recurrence-custom-days"
              type="number"
              min={1}
              value={customIntervalDays}
              onChange={(event) => setCustomIntervalDays(event.target.value)}
              disabled={busy}
            />
          </FormField>
        )}
      </div>

      <div className="task-form-row">
        <FormField label={t('tasks.recurrenceDialog.startDate')} htmlFor="recurrence-start" required error={errors.startDate}>
          <input
            id="recurrence-start"
            type="date"
            value={startDate}
            onChange={(event) => setStartDate(event.target.value)}
            disabled={busy}
          />
        </FormField>
        <FormField label={t('tasks.recurrenceDialog.endDate')} htmlFor="recurrence-end">
          <input
            id="recurrence-end"
            type="date"
            value={endDate}
            onChange={(event) => setEndDate(event.target.value)}
            disabled={busy}
          />
        </FormField>
      </div>

      <label className="task-check-label">
        <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} disabled={busy} />
        {t('tasks.recurrenceDialog.active')}
      </label>
    </Modal>
  )
}
