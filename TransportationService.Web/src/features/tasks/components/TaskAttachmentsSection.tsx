import { useCallback, useEffect, useRef, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  deleteTaskAttachment,
  downloadTaskAttachment,
  listTaskAttachments,
  uploadTaskAttachment,
} from '../api/tasksApi'
import { TASK_ATTACHMENT_ACCEPT, type TaskAttachment } from '../api/types'
import { formatTaskDateTime } from './taskFormat'

interface TaskAttachmentsSectionProps {
  taskId: string
  /** Upload/delete are only offered when the caller may manage the task. */
  canManage: boolean
  /** Notifies the parent so the evidence hint can react to the attachment count. */
  onCountChange?: (count: number) => void
}

function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} kB`
  return `${bytes} B`
}

/** Evidence attachments on a task: list + download + upload (pdf/jpg/png ≤ 10 MB) + delete. */
export function TaskAttachmentsSection({ taskId, canManage, onCountChange }: TaskAttachmentsSectionProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [attachments, setAttachments] = useState<TaskAttachment[]>([])
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const load = useCallback(() => {
    listTaskAttachments(taskId)
      .then((data) => {
        setAttachments(data)
        onCountChange?.(data.length)
      })
      .catch(() => showError(t('tasks.attachments.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [taskId])

  useEffect(() => {
    load()
  }, [load])

  async function handleUpload(file: File) {
    setBusy(true)
    try {
      await uploadTaskAttachment(taskId, file, note.trim() || undefined)
      showSuccess(t('tasks.attachments.uploaded'))
      setNote('')
      load()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.attachments.uploadFailed')))
    } finally {
      setBusy(false)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  async function handleDelete(attachment: TaskAttachment) {
    setBusy(true)
    try {
      await deleteTaskAttachment(taskId, attachment.id)
      showSuccess(t('tasks.attachments.deleted'))
      load()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.attachments.deleteFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="task-detail-section">
      <h4>{t('tasks.attachments.title')}</h4>
      {attachments.length === 0 ? (
        <p className="placeholder-text">{t('tasks.attachments.empty')}</p>
      ) : (
        <ul className="task-attachment-list">
          {attachments.map((attachment) => (
            <li key={attachment.id} className="task-attachment-row">
              <button
                type="button"
                className="ui-button ui-button-ghost"
                onClick={() =>
                  downloadTaskAttachment(taskId, attachment.id, attachment.fileName).catch(() =>
                    showError(t('tasks.attachments.downloadFailed')),
                  )
                }
              >
                {attachment.fileName}
              </button>
              <span className="task-attachment-meta">
                {formatSize(attachment.sizeBytes)} · {formatTaskDateTime(attachment.createdAt)}
                {attachment.note ? ` · ${attachment.note}` : ''}
              </span>
              {canManage && (
                <Button variant="ghost" onClick={() => void handleDelete(attachment)} disabled={busy}>
                  {t('ui.actions.delete')}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {canManage && (
        <div className="task-attachment-upload">
          <input
            type="text"
            placeholder={t('tasks.attachments.notePlaceholder')}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            aria-label={t('tasks.attachments.noteAria')}
            disabled={busy}
          />
          <input
            ref={fileInputRef}
            type="file"
            accept={TASK_ATTACHMENT_ACCEPT}
            aria-label={t('tasks.attachments.uploadAria')}
            disabled={busy}
            onChange={(event) => {
              const file = event.target.files?.[0]
              if (file) void handleUpload(file)
            }}
          />
        </div>
      )}
    </div>
  )
}
