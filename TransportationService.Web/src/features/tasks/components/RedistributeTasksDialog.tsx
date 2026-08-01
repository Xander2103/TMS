import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
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
        if (mounted) showError('Het takenoverzicht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employeeId])

  async function submit() {
    let valid = true
    if (reason.trim().length === 0) {
      setReasonError('Een reden is verplicht.')
      valid = false
    }
    if (action === 'reassign' && !targetEmployeeId) {
      setTargetError('Kies een medewerker.')
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
          ? `${result.affectedTasks} taken herverdeeld.`
          : `${result.affectedTasks} taken geannuleerd.`,
      )
      onClose()
    } catch (err) {
      showError(describeApiError(err, 'Het herverdelen is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`Open taken van ${employeeName}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Sluiten
          </Button>
          <Button variant={action === 'cancel' ? 'danger' : 'primary'} onClick={() => void submit()} disabled={busy}>
            {busy ? 'Bezig...' : 'Bevestigen'}
          </Button>
        </>
      }
    >
      {summary && (
        <div className="task-summary-badges">
          <Badge>Te doen: {summary.todo}</Badge>
          <Badge tone="info">In uitvoering: {summary.inProgress}</Badge>
          <Badge tone="danger">Geblokkeerd: {summary.blocked}</Badge>
          <Badge tone="warning">Wacht op controle: {summary.waitingForReview}</Badge>
          <Badge tone="danger">Achterstallig: {summary.overdue}</Badge>
        </div>
      )}

      <FormField label="Actie" htmlFor="redistribute-action">
        <select
          id="redistribute-action"
          value={action}
          onChange={(event) => setAction(event.target.value as 'reassign' | 'cancel')}
          disabled={busy}
        >
          <option value="reassign">Herverdelen naar een medewerker</option>
          {canCancelAll && <option value="cancel">Alle open taken annuleren</option>}
        </select>
      </FormField>

      {action === 'reassign' && (
        <>
          <FormField label="Nieuwe medewerker" required error={targetError}>
            <EmployeeSelect
              value={targetEmployeeId}
              onChange={(next) => {
                setTargetEmployeeId(next)
                if (targetError && next) setTargetError(undefined)
              }}
              disabled={busy}
              ariaLabel="Nieuwe medewerker"
            />
          </FormField>
          <FormField label="Nieuwe deadline (optioneel)" htmlFor="redistribute-due">
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

      <FormField label="Reden" htmlFor="redistribute-reason" required error={reasonError}>
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
