import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
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
        setError('De taken konden niet worden geladen.')
        setIsLoading(false)
      })
  }, [employeeId])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<EmployeeTask>[] = [
    {
      key: 'title',
      header: 'Titel',
      render: (row) => (
        <span className="task-title-cell">
          {row.title}
          <TaskCategoryBadge name={row.categoryName} color={row.categoryColor} />
        </span>
      ),
    },
    { key: 'priority', header: 'Prioriteit', render: (row) => <TaskPriorityBadge priority={row.priority} /> },
    { key: 'due', header: 'Deadline', render: (row) => <TaskDueCell task={row} /> },
    { key: 'status', header: 'Status', render: (row) => <TaskStatusBadge status={row.status} /> },
  ]

  return (
    <div>
      {summary && (
        <div className="task-summary-badges">
          <Badge>Te doen: {summary.todo}</Badge>
          <Badge tone="info">In uitvoering: {summary.inProgress}</Badge>
          <Badge tone="danger">Geblokkeerd: {summary.blocked}</Badge>
          <Badge tone="warning">Wacht op controle: {summary.waitingForReview}</Badge>
          <Badge tone="danger">Achterstallig: {summary.overdue}</Badge>
        </div>
      )}

      <DataTable
        columns={columns}
        rows={tasks}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Geen taken voor deze medewerker."
        onRowClick={(row) => setOpenTaskId(row.id)}
      />

      {openTaskId && <TaskDetailPanel taskId={openTaskId} onClose={() => setOpenTaskId(null)} onChanged={reload} />}
    </div>
  )
}
