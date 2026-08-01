import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { listTasks } from '../api/tasksApi'
import {
  TASK_PRIORITY_LABELS,
  TASK_STATUS_LABELS,
  type EmployeeTask,
  type TaskPriority,
  type TaskStatus,
} from '../api/types'
import { NewTaskDialog } from '../components/NewTaskDialog'
import { TaskDetailPanel } from '../components/TaskDetailPanel'
import { TaskCategoryBadge, TaskDueCell, TaskPriorityBadge, TaskStatusBadge } from '../components/taskBadges'
import { formatTaskDateTime } from '../components/taskFormat'
import '../components/tasks.css'

const PAGE_SIZE = 25

/**
 * Central task list. Visibility is server-side (own / team / all); `?taskId=` deep-links a
 * detail (notifications link this way) and `?mine=1` pre-activates the own-tasks filter.
 */
export function TasksPage() {
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const canCreate = hasPermission('tasks.manage_own') || hasPermission('tasks.assign')
  const { options: categories } = useLookupOptions('/api/task-categories')

  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'' | TaskStatus>('')
  const [priority, setPriority] = useState<'' | TaskPriority>('')
  const [categoryId, setCategoryId] = useState('')
  const [mine, setMine] = useState(searchParams.get('mine') === '1')
  const [createdByMe, setCreatedByMe] = useState(false)
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [waitingForReviewOnly, setWaitingForReviewOnly] = useState(false)
  const [page, setPage] = useState(1)

  const [tasks, setTasks] = useState<EmployeeTask[]>([])
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNewDialog, setShowNewDialog] = useState(false)

  const openTaskId = searchParams.get('taskId')

  const reload = useCallback(() => {
    listTasks({
      search: search || undefined,
      status: status || undefined,
      priority: priority || undefined,
      categoryId: categoryId || undefined,
      mine: mine || undefined,
      createdByMe: createdByMe || undefined,
      overdueOnly: overdueOnly || undefined,
      waitingForReviewOnly: waitingForReviewOnly || undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((result) => {
        setTasks(result.items)
        setTotal(result.total)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        setError('De taken konden niet worden geladen.')
        setIsLoading(false)
      })
  }, [search, status, priority, categoryId, mine, createdByMe, overdueOnly, waitingForReviewOnly, page])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  function openDetail(taskId: string) {
    const next = new URLSearchParams(searchParams)
    next.set('taskId', taskId)
    setSearchParams(next, { replace: true })
  }

  function closeDetail() {
    const next = new URLSearchParams(searchParams)
    next.delete('taskId')
    setSearchParams(next, { replace: true })
  }

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
    { key: 'assignee', header: 'Toegewezen aan', render: (row) => row.assignedEmployeeName },
    { key: 'priority', header: 'Prioriteit', render: (row) => <TaskPriorityBadge priority={row.priority} /> },
    { key: 'due', header: 'Deadline', render: (row) => <TaskDueCell task={row} /> },
    { key: 'status', header: 'Status', render: (row) => <TaskStatusBadge status={row.status} /> },
    { key: 'updated', header: 'Laatste update', render: (row) => formatTaskDateTime(row.updatedAt) },
  ]

  function toggle(setter: (value: boolean) => void) {
    return (value: boolean) => {
      setter(value)
      setPage(1)
    }
  }

  return (
    <div>
      <PageHeader
        title="Taken"
        subtitle="Toegewezen taken opvolgen, uitvoeren en controleren."
        action={canCreate ? <Button onClick={() => setShowNewDialog(true)}>Nieuwe taak</Button> : undefined}
      />

      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoek op titel of beschrijving..."
      >
        <select
          value={status}
          onChange={(event) => {
            setStatus(event.target.value as '' | TaskStatus)
            setPage(1)
          }}
          aria-label="Status"
        >
          <option value="">Alle statussen</option>
          {Object.entries(TASK_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select
          value={priority}
          onChange={(event) => {
            setPriority(event.target.value as '' | TaskPriority)
            setPage(1)
          }}
          aria-label="Prioriteit"
        >
          <option value="">Alle prioriteiten</option>
          {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select
          value={categoryId}
          onChange={(event) => {
            setCategoryId(event.target.value)
            setPage(1)
          }}
          aria-label="Categorie"
        >
          <option value="">Alle categorieën</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
            </option>
          ))}
        </select>
        <div className="task-filter-toggles">
          <label className="task-filter-toggle">
            <input type="checkbox" checked={mine} onChange={(event) => toggle(setMine)(event.target.checked)} />
            Mijn taken
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={createdByMe}
              onChange={(event) => toggle(setCreatedByMe)(event.target.checked)}
            />
            Door mij toegewezen
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={overdueOnly}
              onChange={(event) => toggle(setOverdueOnly)(event.target.checked)}
            />
            Achterstallig
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={waitingForReviewOnly}
              onChange={(event) => toggle(setWaitingForReviewOnly)(event.target.checked)}
            />
            Wacht op controle
          </label>
        </div>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={tasks}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Geen taken gevonden."
        onRowClick={(row) => openDetail(row.id)}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={total} onPageChange={setPage} />

      {openTaskId && <TaskDetailPanel taskId={openTaskId} onClose={closeDetail} onChanged={reload} />}

      {showNewDialog && (
        <NewTaskDialog
          onClose={() => setShowNewDialog(false)}
          onCreated={() => {
            setShowNewDialog(false)
            reload()
          }}
        />
      )}
    </div>
  )
}
