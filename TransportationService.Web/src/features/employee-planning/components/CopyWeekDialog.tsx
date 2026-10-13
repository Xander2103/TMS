import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { copyWeek } from '../api/employeePlanningApi'
import { shiftWeekStart, type PlanningSelection } from '../planningFilters'
import { mondayOf, toIsoDate } from '../types'

interface CopyWeekDialogProps {
  selection: PlanningSelection
  /** Employees currently on screen for this selection (what will be copied). */
  employeeCount: number
  onClose: () => void
  /** Called with the target week after a successful copy; the page navigates there. */
  onCopied: (targetWeekStart: string) => void
}

/**
 * "Week kopiëren": copies the shifts of the current SELECTION to a target week (as drafts,
 * collisions skipped). The backend resolves the same employee set as the grid, so what the
 * planner sees is what gets copied — the department filter used to drive this already; the
 * selection model simply extends that to every filter.
 */
export function CopyWeekDialog({ selection, employeeCount, onClose, onCopied }: CopyWeekDialogProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [target, setTarget] = useState(() => shiftWeekStart(selection.weekStart, 1))
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!target) {
      showError(t('employeePlanning.copy.targetRequired'))
      return
    }
    setBusy(true)
    try {
      const result = await copyWeek(selection.weekStart, target, {
        departmentIds: selection.departmentIds,
        jobFunctionIds: selection.jobFunctionIds,
        contractTypeIds: selection.contractTypeIds,
        statuses: selection.statuses,
        driversOnly: selection.driversOnly,
        employeeIds: selection.employeeIds,
      })
      showSuccess(t('employeePlanning.copy.success', { count: result.copiedCount, skipped: result.skippedCount }))
      onCopied(target)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('employeePlanning.copy.failed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('employeePlanning.copy.title', { week: selection.weekStart })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="ep-copy-form" disabled={busy}>
            {busy ? t('employeePlanning.dialog.busy') : t('employeePlanning.copy.copy')}
          </Button>
        </>
      }
    >
      <form id="ep-copy-form" className="ep-form" onSubmit={handleSubmit} noValidate>
        <p className="ep-copy-scope">{t('employeePlanning.copy.scope', { count: employeeCount })}</p>
        <FormField label={t('employeePlanning.copy.targetLabel')} htmlFor="ep-copy-target" required hint={t('employeePlanning.copy.targetHint')}>
          <input
            id="ep-copy-target"
            type="date"
            value={target}
            onChange={(e) => e.target.value && setTarget(toIsoDate(mondayOf(new Date(`${e.target.value}T00:00:00`))))}
            disabled={busy}
          />
        </FormField>
      </form>
    </Modal>
  )
}
