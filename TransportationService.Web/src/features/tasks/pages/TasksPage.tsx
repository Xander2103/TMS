import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
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
 * detail (notifications link this way) and `?mine=1`, `?overdue=1`, `?review=1` and
 * `?status=` (dashboardtegels) pre-activate the matching filters.
 */
export function TasksPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const canCreate = hasPermission('tasks.manage_own') || hasPermission('tasks.assign')
  const { options: categories } = useLookupOptions('/api/task-categories')

  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'' | TaskStatus>(() => {
    const requested = searchParams.get('status')
    return requested && requested in TASK_STATUS_LABELS ? (requested as TaskStatus) : ''
  })
  const [priority, setPriority] = useState<'' | TaskPriority>('')
  const [categoryId, setCategoryId] = useState('')
  const [mine, setMine] = useState(searchParams.get('mine') === '1')
  const [createdByMe, setCreatedByMe] = useState(false)
  const [overdueOnly, setOverdueOnly] = useState(searchParams.get('overdue') === '1')
  const [waitingForReviewOnly, setWaitingForReviewOnly] = useState(searchParams.get('review') === '1')
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
        setError(t('tasks.page.loadFailed'))
        setIsLoading(false)
      })
  }, [search, status, priority, categoryId, mine, createdByMe, overdueOnly, waitingForReviewOnly, page, t])

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
      header: t('tasks.columns.title'),
      render: (row) => (
        <span className="task-title-cell">
          {row.title}
          <TaskCategoryBadge name={row.categoryName} color={row.categoryColor} />
        </span>
      ),
    },
    { key: 'assignee', header: t('tasks.columns.assignee'), render: (row) => row.assignedEmployeeName },
    { key: 'priority', header: t('tasks.columns.priority'), render: (row) => <TaskPriorityBadge priority={row.priority} /> },
    { key: 'due', header: t('tasks.columns.due'), render: (row) => <TaskDueCell task={row} /> },
    { key: 'status', header: t('tasks.columns.status'), render: (row) => <TaskStatusBadge status={row.status} /> },
    { key: 'updated', header: t('tasks.columns.updated'), render: (row) => formatTaskDateTime(row.updatedAt) },
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
        title={t('tasks.page.title')}
        subtitle={t('tasks.page.subtitle')}
        action={canCreate ? <Button onClick={() => setShowNewDialog(true)}>{t('tasks.page.newTask')}</Button> : undefined}
      />

      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder={t('tasks.page.searchPlaceholder')}
      >
        <select
          value={status}
          onChange={(event) => {
            setStatus(event.target.value as '' | TaskStatus)
            setPage(1)
          }}
          aria-label={t('tasks.page.statusFilter')}
        >
          <option value="">{t('tasks.page.allStatuses')}</option>
          {Object.entries(TASK_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {t(label)}
            </option>
          ))}
        </select>
        <select
          value={priority}
          onChange={(event) => {
            setPriority(event.target.value as '' | TaskPriority)
            setPage(1)
          }}
          aria-label={t('tasks.page.priorityFilter')}
        >
          <option value="">{t('tasks.page.allPriorities')}</option>
          {Object.entries(TASK_PRIORITY_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {t(label)}
            </option>
          ))}
        </select>
        <select
          value={categoryId}
          onChange={(event) => {
            setCategoryId(event.target.value)
            setPage(1)
          }}
          aria-label={t('tasks.page.categoryFilter')}
        >
          <option value="">{t('tasks.page.allCategories')}</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
            </option>
          ))}
        </select>
        <div className="task-filter-toggles">
          <label className="task-filter-toggle">
            <input type="checkbox" checked={mine} onChange={(event) => toggle(setMine)(event.target.checked)} />
            {t('tasks.page.filterMine')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={createdByMe}
              onChange={(event) => toggle(setCreatedByMe)(event.target.checked)}
            />
            {t('tasks.page.filterCreatedByMe')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={overdueOnly}
              onChange={(event) => toggle(setOverdueOnly)(event.target.checked)}
            />
            {t('tasks.page.filterOverdue')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={waitingForReviewOnly}
              onChange={(event) => toggle(setWaitingForReviewOnly)(event.target.checked)}
            />
            {t('tasks.page.filterReview')}
          </label>
        </div>
      </FilterBar>

      <DataTable
        columns={columns}
        rows={tasks}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('tasks.page.empty')}
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
