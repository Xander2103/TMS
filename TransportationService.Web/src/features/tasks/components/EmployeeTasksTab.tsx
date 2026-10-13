import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import { getEmployeeOpenTaskSummary, listEmployeeTasks } from '../api/tasksApi'
import type { EmployeeTask, TaskOpenSummary } from '../api/types'
import { TaskDetailPanel } from './TaskDetailPanel'
import { TaskCategoryBadge, TaskDueCell, TaskPriorityBadge, TaskStatusBadge } from './taskBadges'
import './tasks.css'

interface EmployeeTasksTabProps {
  employeeId: string
}

/** "Taken" tab on the personnel dossier: open-summary badges + the employee's task list. */
export function EmployeeTasksTab({ employeeId }: EmployeeTasksTabProps) {
  const { t } = useLocale()
  const [summary, setSummary] = useState<TaskOpenSummary | null>(null)
  const [tasks, setTasks] = useState<EmployeeTask[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)

  const reload = useCallback(() => {
    Promise.all([getEmployeeOpenTaskSummary(employeeId), listEmployeeTasks(employeeId)])
      .then(([summaryData, taskData]) => {
        setSummary(summaryData)
        setTasks(taskData)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        setError(t('tasks.page.loadFailed'))
        setIsLoading(false)
      })
  }, [employeeId, t])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<EmployeeTask>[] = [
    {
      key: 'title',
      header: t('tasks.columns.title'),
      render: (row) => (
        <span className="task-title-cell">
          {row.title}
          <TaskCategoryBadge name={row.categoryName} color={row.categoryColor} />
        </span>
      ),
    },
    { key: 'priority', header: t('tasks.columns.priority'), render: (row) => <TaskPriorityBadge priority={row.priority} /> },
    { key: 'due', header: t('tasks.columns.due'), render: (row) => <TaskDueCell task={row} /> },
    { key: 'status', header: t('tasks.columns.status'), render: (row) => <TaskStatusBadge status={row.status} /> },
  ]

  return (
    <div>
      {summary && (
        <div className="task-summary-badges">
          <Badge>{t('tasks.summary.todo', { count: summary.todo })}</Badge>
          <Badge tone="info">{t('tasks.summary.inProgress', { count: summary.inProgress })}</Badge>
          <Badge tone="danger">{t('tasks.summary.blocked', { count: summary.blocked })}</Badge>
          <Badge tone="warning">{t('tasks.summary.waitingForReview', { count: summary.waitingForReview })}</Badge>
          <Badge tone="danger">{t('tasks.summary.overdue', { count: summary.overdue })}</Badge>
        </div>
      )}

      <DataTable
        columns={columns}
        rows={tasks}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('tasks.employeeTab.empty')}
        onRowClick={(row) => setOpenTaskId(row.id)}
      />

      {openTaskId && <TaskDetailPanel taskId={openTaskId} onClose={() => setOpenTaskId(null)} onChanged={reload} />}
    </div>
  )
}
