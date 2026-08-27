import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { formatTaskDateTime } from './taskFormat'
import {
  TASK_PRIORITY_LABELS,
  TASK_PRIORITY_TONES,
  TASK_STATUS_LABELS,
  TASK_STATUS_TONES,
  type EmployeeTask,
  type TaskPriority,
  type TaskStatus,
} from '../api/types'

export function TaskStatusBadge({ status }: { status: TaskStatus }) {
  const { t } = useLocale()
  return <Badge tone={TASK_STATUS_TONES[status]}>{t(TASK_STATUS_LABELS[status])}</Badge>
}

export function TaskPriorityBadge({ priority }: { priority: TaskPriority }) {
  const { t } = useLocale()
  return <Badge tone={TASK_PRIORITY_TONES[priority]}>{t(TASK_PRIORITY_LABELS[priority])}</Badge>
}

/** Category pill; uses the category colour when set, neutral otherwise. */
export function TaskCategoryBadge({ name, color }: { name: string | null; color: string | null }) {
  if (!name) return null
  if (!color) return <Badge>{name}</Badge>
  return (
    <span className="task-category-badge" style={{ backgroundColor: color }}>
      {name}
    </span>
  )
}

/** Deadline cell: overdue tasks render red with an explicit warning label. */
export function TaskDueCell({ task }: { task: Pick<EmployeeTask, 'dueAt' | 'isOverdue'> }) {
  const { t } = useLocale()
  if (!task.dueAt) return <>—</>
  if (!task.isOverdue) return <>{formatTaskDateTime(task.dueAt)}</>
  return (
    <span className="task-due-overdue">
      {formatTaskDateTime(task.dueAt)} <Badge tone="danger">{t('tasks.badges.overdue')}</Badge>
    </span>
  )
}
