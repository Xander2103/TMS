import { useCallback, useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { Modal } from '../../../components/ui/Modal'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import {
  blockTask,
  cancelTask,
  completeTask,
  getTask,
  reopenTask,
  resumeTask,
  reviewTask,
  startTask,
  submitTaskForReview,
} from '../api/tasksApi'
import { OPEN_TASK_STATUSES, type EmployeeTask } from '../api/types'
import { TaskAttachmentsSection } from './TaskAttachmentsSection'
import { TaskNoteDialog } from './TaskNoteDialog'
import { TaskCategoryBadge, TaskDueCell, TaskPriorityBadge, TaskStatusBadge } from './taskBadges'
import { formatTaskDateTime } from './taskFormat'
import './tasks.css'

interface TaskDetailPanelProps {
  taskId: string
  onClose: () => void
  /** Called after every successful or conflicting mutation so the surrounding list refreshes. */
  onChanged?: () => void
}

type NoteDialogKind = 'block' | 'complete' | 'reject' | null

/**
 * Task detail modal: all fields, evidence attachments and the status-action buttons.
 * Every action sends expectedVersion = task.version; a 400 (conflict/illegal transition)
 * surfaces the backend's NL message as a toast and reloads the task.
 */
export function TaskDetailPanel({ taskId, onClose, onChanged }: TaskDetailPanelProps) {
  const { user, hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const [task, setTask] = useState<EmployeeTask | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [noteDialog, setNoteDialog] = useState<NoteDialogKind>(null)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [attachmentCount, setAttachmentCount] = useState(0)

  const load = useCallback(() => {
    getTask(taskId)
      .then((data) => {
        setTask(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De taak kon niet worden geladen.'))
  }, [taskId])

  useEffect(() => {
    load()
  }, [load])

  const isAssignee = user?.employeeId != null && task != null && user.employeeId === task.assignedEmployeeId
  const isCreator = user?.id != null && task != null && user.id === task.createdByUserId
  const isOpen = task != null && OPEN_TASK_STATUSES.includes(task.status)
  // Mirrors the backend: assignees act on their own tasks; tasks.edit unlocks others' tasks.
  const canExecute = task != null && (isAssignee || hasPermission('tasks.edit'))
  const canReview = task != null && !isAssignee && (isCreator || hasPermission('tasks.review'))
  const canCancel = task != null && (isCreator || hasPermission('tasks.cancel'))
  const canReopen = hasPermission('tasks.reopen')
  const canManageAttachments = canExecute

  /** Runs a status action, always refreshing the task afterwards (also on 400 conflicts). */
  async function run(action: (expectedVersion: number) => Promise<EmployeeTask>, successMessage: string) {
    if (!task) return
    setBusy(true)
    try {
      const updated = await action(task.version)
      setTask(updated)
      showSuccess(successMessage)
      setNoteDialog(null)
      setConfirmCancel(false)
    } catch (err) {
      // Version conflict or illegal transition: show the backend's NL detail and reload.
      showError(describeApiError(err, 'De actie kon niet worden uitgevoerd.').message)
      setNoteDialog(null)
      setConfirmCancel(false)
      load()
    } finally {
      setBusy(false)
      onChanged?.()
    }
  }

  if (loadError) {
    return (
      <Modal title="Taak" onClose={onClose}>
        <ErrorState message={loadError} />
      </Modal>
    )
  }

  if (!task) {
    return (
      <Modal title="Taak" onClose={onClose}>
        <LoadingState message="Taak laden..." />
      </Modal>
    )
  }

  const evidenceMissing = task.requiresEvidence && attachmentCount === 0

  return (
    <Modal title={task.title} onClose={onClose} busy={busy}>
      <div className="task-detail-badges">
        <TaskStatusBadge status={task.status} />
        <TaskPriorityBadge priority={task.priority} />
        <TaskCategoryBadge name={task.categoryName} color={task.categoryColor} />
      </div>

      {task.description && <p className="task-detail-description">{task.description}</p>}

      <dl className="task-detail-grid">
        <div>
          <dt>Toegewezen aan</dt>
          <dd>{task.assignedEmployeeName}</dd>
        </div>
        <div>
          <dt>Aangemaakt door</dt>
          <dd>{task.createdByName ?? '—'}</dd>
        </div>
        <div>
          <dt>Start</dt>
          <dd>{formatTaskDateTime(task.startAt)}</dd>
        </div>
        <div>
          <dt>Deadline</dt>
          <dd>
            <TaskDueCell task={task} />
          </dd>
        </div>
        {task.completedAt && (
          <div>
            <dt>Voltooid op</dt>
            <dd>{formatTaskDateTime(task.completedAt)}</dd>
          </div>
        )}
        {task.cancelledAt && (
          <div>
            <dt>Geannuleerd op</dt>
            <dd>{formatTaskDateTime(task.cancelledAt)}</dd>
          </div>
        )}
        <div>
          <dt>Laatste update</dt>
          <dd>{formatTaskDateTime(task.updatedAt)}</dd>
        </div>
        <div>
          <dt>Vereisten</dt>
          <dd>
            {[
              task.requiresReview ? 'controle' : null,
              task.requiresCompletionNote ? 'notitie bij voltooien' : null,
              task.requiresEvidence ? 'bewijs (bijlage)' : null,
            ]
              .filter(Boolean)
              .join(', ') || 'Geen'}
          </dd>
        </div>
      </dl>

      {task.blockedReason && task.status === 'Blocked' && (
        <div className="task-detail-note is-danger">
          <strong>Geblokkeerd:</strong> {task.blockedReason}
        </div>
      )}
      {task.completionNote && (
        <div className="task-detail-note">
          <strong>Notitie bij voltooien:</strong> {task.completionNote}
        </div>
      )}
      {task.reviewNote && (
        <div className="task-detail-note">
          <strong>Controlenotitie:</strong> {task.reviewNote}
        </div>
      )}

      <TaskAttachmentsSection taskId={task.id} canManage={canManageAttachments} onCountChange={setAttachmentCount} />

      <div className="task-detail-actions">
        {canExecute && task.status === 'Todo' && (
          <Button onClick={() => void run((v) => startTask(task.id, { expectedVersion: v }), 'Taak gestart.')} disabled={busy}>
            Start
          </Button>
        )}
        {canExecute && task.status === 'InProgress' && (
          <>
            {task.requiresReview ? (
              <Button
                onClick={() =>
                  void run((v) => submitTaskForReview(task.id, { expectedVersion: v }), 'Taak ingediend ter controle.')
                }
                disabled={busy}
              >
                Indienen ter controle
              </Button>
            ) : (
              <Button onClick={() => setNoteDialog('complete')} disabled={busy}>
                Voltooi
              </Button>
            )}
            <Button variant="secondary" onClick={() => setNoteDialog('block')} disabled={busy}>
              Blokkeer
            </Button>
          </>
        )}
        {canExecute && task.status === 'Blocked' && (
          <Button onClick={() => void run((v) => resumeTask(task.id, { expectedVersion: v }), 'Taak hervat.')} disabled={busy}>
            Hervat
          </Button>
        )}
        {canReview && task.status === 'WaitingForReview' && (
          <>
            <Button
              onClick={() =>
                void run((v) => reviewTask(task.id, { expectedVersion: v, approve: true }), 'Taak goedgekeurd.')
              }
              disabled={busy}
            >
              Goedkeuren
            </Button>
            <Button variant="secondary" onClick={() => setNoteDialog('reject')} disabled={busy}>
              Afkeuren
            </Button>
          </>
        )}
        {canCancel && isOpen && (
          <Button variant="danger" onClick={() => setConfirmCancel(true)} disabled={busy}>
            Annuleren
          </Button>
        )}
        {canReopen && (task.status === 'Completed' || task.status === 'Cancelled') && (
          <Button variant="secondary" onClick={() => void run((v) => reopenTask(task.id, { expectedVersion: v }), 'Taak heropend.')} disabled={busy}>
            Heropen
          </Button>
        )}
      </div>

      {noteDialog === 'block' && (
        <TaskNoteDialog
          title="Taak blokkeren"
          label="Reden"
          confirmLabel="Blokkeer"
          requireNote
          destructive
          busy={busy}
          onSubmit={(note) => void run((v) => blockTask(task.id, { expectedVersion: v, note }), 'Taak geblokkeerd.')}
          onClose={() => setNoteDialog(null)}
        />
      )}

      {noteDialog === 'complete' && (
        <TaskNoteDialog
          title="Taak voltooien"
          label="Notitie"
          confirmLabel="Voltooi"
          requireNote={task.requiresCompletionNote}
          hint={evidenceMissing ? 'Deze taak vereist bewijs: voeg eerst een bijlage toe.' : undefined}
          busy={busy}
          onSubmit={(note) =>
            void run((v) => completeTask(task.id, { expectedVersion: v, note: note || undefined }), 'Taak voltooid.')
          }
          onClose={() => setNoteDialog(null)}
        />
      )}

      {noteDialog === 'reject' && (
        <TaskNoteDialog
          title="Taak afkeuren"
          label="Reden"
          confirmLabel="Afkeuren"
          requireNote
          destructive
          busy={busy}
          onSubmit={(note) =>
            void run((v) => reviewTask(task.id, { expectedVersion: v, approve: false, note }), 'Taak afgekeurd.')
          }
          onClose={() => setNoteDialog(null)}
        />
      )}

      {confirmCancel && (
        <ConfirmDialog
          title="Taak annuleren"
          message={`"${task.title}" annuleren? De taak blijft zichtbaar in de historiek.`}
          confirmLabel="Taak annuleren"
          destructive
          busy={busy}
          onConfirm={() => void run((v) => cancelTask(task.id, { expectedVersion: v }), 'Taak geannuleerd.')}
          onCancel={() => setConfirmCancel(false)}
        />
      )}
    </Modal>
  )
}
