import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getEmployeeOpenTaskSummary, redistributeTasks } from '../api/tasksApi'
import type { TaskOpenSummary } from '../api/types'
import { EmployeeSelect } from './EmployeePicker'
import './tasks.css'

interface RedistributeTasksDialogProps {
  employeeId: string
  employeeName: string
  onClose: () => void
}

/**
 * Redistributes (or cancels) all open tasks of one employee, typically after deactivation.
 * Reassign needs tasks.assign (page-level gate); the cancel action additionally needs tasks.cancel.
 */
export function RedistributeTasksDialog({ employeeId, employeeName, onClose }: RedistributeTasksDialogProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canCancelAll = hasPermission('tasks.cancel')

  const [summary, setSummary] = useState<TaskOpenSummary | null>(null)
  const [action, setAction] = useState<'reassign' | 'cancel'>('reassign')
  const [targetEmployeeId, setTargetEmployeeId] = useState<string | null>(null)
  const [newDueAt, setNewDueAt] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [reasonError, setReasonError] = useState<string | undefined>()
  const [targetError, setTargetError] = useState<string | undefined>()

  useEffect(() => {
    let mounted = true
    getEmployeeOpenTaskSummary(employeeId)
      .then((data) => {
        if (mounted) setSummary(data)
      })
      .catch(() => {
        if (mounted) showError(t('tasks.redistribute.summaryFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employeeId])

  async function submit() {
    let valid = true
    if (reason.trim().length === 0) {
      setReasonError(t('tasks.redistribute.reasonRequired'))
      valid = false
    }
    if (action === 'reassign' && !targetEmployeeId) {
      setTargetError(t('tasks.redistribute.chooseEmployee'))
      valid = false
    }
    if (!valid) return

    setBusy(true)
    try {
      const result = await redistributeTasks({
        fromEmployeeId: employeeId,
        action,
        targetEmployeeId: action === 'reassign' ? (targetEmployeeId ?? undefined) : undefined,
        newDueAt: action === 'reassign' && newDueAt ? newDueAt : undefined,
        reason: reason.trim(),
      })
      showSuccess(
        action === 'reassign'
          ? t('tasks.redistribute.reassigned', { count: result.affectedTasks })
          : t('tasks.redistribute.cancelled', { count: result.affectedTasks }),
      )
      onClose()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.redistribute.failed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('tasks.redistribute.title', { name: employeeName })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.close')}
          </Button>
          <Button variant={action === 'cancel' ? 'danger' : 'primary'} onClick={() => void submit()} disabled={busy}>
            {busy ? t('ui.actions.busy') : t('ui.actions.confirm')}
          </Button>
        </>
      }
    >
      {summary && (
        <div className="task-summary-badges">
          <Badge>{t('tasks.summary.todo', { count: summary.todo })}</Badge>
          <Badge tone="info">{t('tasks.summary.inProgress', { count: summary.inProgress })}</Badge>
          <Badge tone="danger">{t('tasks.summary.blocked', { count: summary.blocked })}</Badge>
          <Badge tone="warning">{t('tasks.summary.waitingForReview', { count: summary.waitingForReview })}</Badge>
          <Badge tone="danger">{t('tasks.summary.overdue', { count: summary.overdue })}</Badge>
        </div>
      )}

      <FormField label={t('tasks.redistribute.action')} htmlFor="redistribute-action">
        <select
          id="redistribute-action"
          value={action}
          onChange={(event) => setAction(event.target.value as 'reassign' | 'cancel')}
          disabled={busy}
        >
          <option value="reassign">{t('tasks.redistribute.actionReassign')}</option>
          {canCancelAll && <option value="cancel">{t('tasks.redistribute.actionCancel')}</option>}
        </select>
      </FormField>

      {action === 'reassign' && (
        <>
          <FormField label={t('tasks.redistribute.newEmployee')} required error={targetError}>
            <EmployeeSelect
              value={targetEmployeeId}
              onChange={(next) => {
                setTargetEmployeeId(next)
                if (targetError && next) setTargetError(undefined)
              }}
              disabled={busy}
              ariaLabel={t('tasks.redistribute.newEmployee')}
            />
          </FormField>
          <FormField label={t('tasks.redistribute.newDue')} htmlFor="redistribute-due">
            <input
              id="redistribute-due"
              type="datetime-local"
              value={newDueAt}
              onChange={(event) => setNewDueAt(event.target.value)}
              disabled={busy}
            />
          </FormField>
        </>
      )}

      <FormField label={t('tasks.redistribute.reason')} htmlFor="redistribute-reason" required error={reasonError}>
        <textarea
          id="redistribute-reason"
          rows={3}
          value={reason}
          onChange={(event) => {
            setReason(event.target.value)
            if (reasonError && event.target.value.trim()) setReasonError(undefined)
          }}
          disabled={busy}
        />
      </FormField>
    </Modal>
  )
}
