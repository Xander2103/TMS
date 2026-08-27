import { useCallback, useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { Modal } from '../../../components/ui/Modal'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
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
 * surfaces the backend's message as a toast and reloads the task.
 */
export function TaskDetailPanel({ taskId, onClose, onChanged }: TaskDetailPanelProps) {
  const { t } = useLocale()
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
      .catch(() => setLoadError(t('tasks.detail.loadFailed')))
  }, [taskId, t])

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
      // Version conflict or illegal transition: show the backend's detail and reload.
      showError(localizeApiError(t, err, t('tasks.detail.actionFailed')))
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
      <Modal title={t('tasks.detail.fallbackTitle')} onClose={onClose}>
        <ErrorState message={loadError} />
      </Modal>
    )
  }

  if (!task) {
    return (
      <Modal title={t('tasks.detail.fallbackTitle')} onClose={onClose}>
        <LoadingState message={t('tasks.detail.loading')} />
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
          <dt>{t('tasks.detail.assignedTo')}</dt>
          <dd>{task.assignedEmployeeName}</dd>
        </div>
        <div>
          <dt>{t('tasks.detail.createdBy')}</dt>
          <dd>{task.createdByName ?? '—'}</dd>
        </div>
        <div>
          <dt>{t('tasks.detail.start')}</dt>
          <dd>{formatTaskDateTime(task.startAt)}</dd>
        </div>
        <div>
          <dt>{t('tasks.detail.due')}</dt>
          <dd>
            <TaskDueCell task={task} />
          </dd>
        </div>
        {task.completedAt && (
          <div>
            <dt>{t('tasks.detail.completedAt')}</dt>
            <dd>{formatTaskDateTime(task.completedAt)}</dd>
          </div>
        )}
        {task.cancelledAt && (
          <div>
            <dt>{t('tasks.detail.cancelledAt')}</dt>
            <dd>{formatTaskDateTime(task.cancelledAt)}</dd>
          </div>
        )}
        <div>
          <dt>{t('tasks.detail.updated')}</dt>
          <dd>{formatTaskDateTime(task.updatedAt)}</dd>
        </div>
        <div>
          <dt>{t('tasks.detail.requirements')}</dt>
          <dd>
            {[
              task.requiresReview ? t('tasks.detail.requirementReview') : null,
              task.requiresCompletionNote ? t('tasks.detail.requirementNote') : null,
              task.requiresEvidence ? t('tasks.detail.requirementEvidence') : null,
            ]
              .filter(Boolean)
              .join(', ') || t('tasks.detail.requirementsNone')}
          </dd>
        </div>
      </dl>

      {task.blockedReason && task.status === 'Blocked' && (
        <div className="task-detail-note is-danger">
          <strong>{t('tasks.detail.blockedLabel')}</strong> {task.blockedReason}
        </div>
      )}
      {task.completionNote && (
        <div className="task-detail-note">
          <strong>{t('tasks.detail.completionNoteLabel')}</strong> {task.completionNote}
        </div>
      )}
      {task.reviewNote && (
        <div className="task-detail-note">
          <strong>{t('tasks.detail.reviewNoteLabel')}</strong> {task.reviewNote}
        </div>
      )}

      <TaskAttachmentsSection taskId={task.id} canManage={canManageAttachments} onCountChange={setAttachmentCount} />

      <div className="task-detail-actions">
        {canExecute && task.status === 'Todo' && (
          <Button
            onClick={() => void run((v) => startTask(task.id, { expectedVersion: v }), t('tasks.toasts.started'))}
            disabled={busy}
          >
            {t('tasks.actions.start')}
          </Button>
        )}
        {canExecute && task.status === 'InProgress' && (
          <>
            {task.requiresReview ? (
              <Button
                onClick={() =>
                  void run(
                    (v) => submitTaskForReview(task.id, { expectedVersion: v }),
                    t('tasks.toasts.submittedForReview'),
                  )
                }
                disabled={busy}
              >
                {t('tasks.actions.submitForReview')}
              </Button>
            ) : (
              <Button onClick={() => setNoteDialog('complete')} disabled={busy}>
                {t('tasks.actions.complete')}
              </Button>
            )}
            <Button variant="secondary" onClick={() => setNoteDialog('block')} disabled={busy}>
              {t('tasks.actions.block')}
            </Button>
          </>
        )}
        {canExecute && task.status === 'Blocked' && (
          <Button
            onClick={() => void run((v) => resumeTask(task.id, { expectedVersion: v }), t('tasks.toasts.resumed'))}
            disabled={busy}
          >
            {t('tasks.actions.resume')}
          </Button>
        )}
        {canReview && task.status === 'WaitingForReview' && (
          <>
            <Button
              onClick={() =>
                void run((v) => reviewTask(task.id, { expectedVersion: v, approve: true }), t('tasks.toasts.approved'))
              }
              disabled={busy}
            >
              {t('tasks.actions.approve')}
            </Button>
            <Button variant="secondary" onClick={() => setNoteDialog('reject')} disabled={busy}>
              {t('tasks.actions.reject')}
            </Button>
          </>
        )}
        {canCancel && isOpen && (
          <Button variant="danger" onClick={() => setConfirmCancel(true)} disabled={busy}>
            {t('tasks.actions.cancel')}
          </Button>
        )}
        {canReopen && (task.status === 'Completed' || task.status === 'Cancelled') && (
          <Button
            variant="secondary"
            onClick={() => void run((v) => reopenTask(task.id, { expectedVersion: v }), t('tasks.toasts.reopened'))}
            disabled={busy}
          >
            {t('tasks.actions.reopen')}
          </Button>
        )}
      </div>

      {noteDialog === 'block' && (
        <TaskNoteDialog
          title={t('tasks.noteDialog.blockTitle')}
          label={t('tasks.noteDialog.reason')}
          confirmLabel={t('tasks.actions.block')}
          requireNote
          destructive
          busy={busy}
          onSubmit={(note) => void run((v) => blockTask(task.id, { expectedVersion: v, note }), t('tasks.toasts.blocked'))}
          onClose={() => setNoteDialog(null)}
        />
      )}

      {noteDialog === 'complete' && (
        <TaskNoteDialog
          title={t('tasks.noteDialog.completeTitle')}
          label={t('tasks.noteDialog.note')}
          confirmLabel={t('tasks.actions.complete')}
          requireNote={task.requiresCompletionNote}
          hint={evidenceMissing ? t('tasks.noteDialog.evidenceHint') : undefined}
          busy={busy}
          onSubmit={(note) =>
            void run((v) => completeTask(task.id, { expectedVersion: v, note: note || undefined }), t('tasks.toasts.completed'))
          }
          onClose={() => setNoteDialog(null)}
        />
      )}

      {noteDialog === 'reject' && (
        <TaskNoteDialog
          title={t('tasks.noteDialog.rejectTitle')}
          label={t('tasks.noteDialog.reason')}
          confirmLabel={t('tasks.actions.reject')}
          requireNote
          destructive
          busy={busy}
          onSubmit={(note) =>
            void run((v) => reviewTask(task.id, { expectedVersion: v, approve: false, note }), t('tasks.toasts.rejected'))
          }
          onClose={() => setNoteDialog(null)}
        />
      )}

      {confirmCancel && (
        <ConfirmDialog
          title={t('tasks.cancelDialog.title')}
          message={t('tasks.cancelDialog.message', { title: task.title })}
          confirmLabel={t('tasks.cancelDialog.confirm')}
          destructive
          busy={busy}
          onConfirm={() => void run((v) => cancelTask(task.id, { expectedVersion: v }), t('tasks.toasts.cancelled'))}
          onCancel={() => setConfirmCancel(false)}
        />
      )}
    </Modal>
  )
}
