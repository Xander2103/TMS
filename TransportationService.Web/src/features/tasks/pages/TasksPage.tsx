import { useCallback, useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
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
 * B26: the whole list state lives in the URL, under the names the dashboard tiles and
 * notifications already link with (`?mine=1`, `?overdue=1`, `?review=1`, `?status=`, `?taskId=`).
 */
const LIST_STATE = {
  defaults: { search: '', status: '', priority: '', category: '', mine: '', created: '', overdue: '', review: '', sort: '', dir: '', taskId: '' },
  allowed: {
    status: Object.keys(TASK_STATUS_LABELS),
    priority: Object.keys(TASK_PRIORITY_LABELS),
    mine: ['1'],
    created: ['1'],
    overdue: ['1'],
    review: ['1'],
    sort: ['due', 'priority', 'created', 'title'],
    dir: ['asc', 'desc'],
  },
}
const DESC_BY_DEFAULT = ['priority', 'created']

/**
 * Central task list. Visibility is server-side (own / team / all); `?taskId=` deep-links a
 * detail (notifications link this way) and `?mine=1`, `?overdue=1`, `?review=1` and
 * `?status=` (dashboardtegels) pre-activate the matching filters.
 */
export function TasksPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const [density, setDensity] = useListDensity('tasks')
  const canCreate = hasPermission('tasks.manage_own') || hasPermission('tasks.assign')
  const { options: categories } = useLookupOptions('/api/task-categories')

  const { search, page } = state
  const status = state.status as '' | TaskStatus
  const priority = state.priority as '' | TaskPriority
  const categoryId = state.category
  const mine = state.mine === '1'
  const createdByMe = state.created === '1'
  const overdueOnly = state.overdue === '1'
  const waitingForReviewOnly = state.review === '1'

  const [tasks, setTasks] = useState<EmployeeTask[]>([])
  const [total, setTotal] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showNewDialog, setShowNewDialog] = useState(false)

  const openTaskId = state.taskId || null

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
      sort: state.sort,
      dir: state.dir as 'asc' | 'desc' | '',
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
  }, [search, status, priority, categoryId, mine, createdByMe, overdueOnly, waitingForReviewOnly, page, state.sort, state.dir, t])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  // The open detail is part of the URL too, without leaving the current page of the list.
  function openDetail(taskId: string) {
    apply({ taskId, page }, { replace: true })
  }

  function closeDetail() {
    apply({ taskId: '', page }, { replace: true })
  }

  // B50: one line per task — title (+ category) truncated with its description as the tooltip,
  // assignee, priority, due (overdue stays visible), status. The description itself lives in the detail.
  const columns: Column<EmployeeTask>[] = [
    {
      key: 'title',
      header: t('tasks.columns.title'),
      sortKey: 'title',
      render: (row) => (
        <span className="task-title-cell">
          <TruncatedText tooltip={row.description ? `${row.title} — ${row.description}` : row.title} alwaysShowTooltip={Boolean(row.description)}>
            {row.title}
          </TruncatedText>
          <TaskCategoryBadge name={row.categoryName} color={row.categoryColor} />
        </span>
      ),
    },
    { key: 'assignee', header: t('tasks.columns.assignee'), width: '170px', render: (row) => <TruncatedText tooltip={row.assignedEmployeeName}>{row.assignedEmployeeName}</TruncatedText> },
    { key: 'priority', header: t('tasks.columns.priority'), width: '110px', sortKey: 'priority', render: (row) => <TaskPriorityBadge priority={row.priority} /> },
    { key: 'due', header: t('tasks.columns.due'), width: '180px', sortKey: 'due', render: (row) => <span className="ui-nowrap"><TaskDueCell task={row} compact /></span> },
    { key: 'status', header: t('tasks.columns.status'), width: '170px', render: (row) => <TaskStatusBadge status={row.status} /> },
    { key: 'updated', header: t('tasks.columns.updated'), width: '150px', sortKey: 'created', render: (row) => <span className="ui-nowrap">{formatTaskDateTime(row.updatedAt)}</span> },
  ]

  function toggle(key: 'mine' | 'created' | 'overdue' | 'review') {
    return (value: boolean) => apply({ [key]: value ? '1' : '' })
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
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('tasks.page.searchPlaceholder')}
        onClear={isFiltered && (state.search || status || priority || categoryId || mine || createdByMe || overdueOnly || waitingForReviewOnly || state.sort) ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      >
        <select
          className="ui-filter-select"
          value={status}
          onChange={(event) => apply({ status: event.target.value })}
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
          className="ui-filter-select"
          value={priority}
          onChange={(event) => apply({ priority: event.target.value })}
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
          className="ui-filter-select"
          value={categoryId}
          onChange={(event) => apply({ category: event.target.value })}
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
            <input type="checkbox" checked={mine} onChange={(event) => toggle('mine')(event.target.checked)} />
            {t('tasks.page.filterMine')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={createdByMe}
              onChange={(event) => toggle('created')(event.target.checked)}
            />
            {t('tasks.page.filterCreatedByMe')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={overdueOnly}
              onChange={(event) => toggle('overdue')(event.target.checked)}
            />
            {t('tasks.page.filterOverdue')}
          </label>
          <label className="task-filter-toggle">
            <input
              type="checkbox"
              checked={waitingForReviewOnly}
              onChange={(event) => toggle('review')(event.target.checked)}
            />
            {t('tasks.page.filterReview')}
          </label>
        </div>
      </FilterBar>

      <DataTable
        fixedLayout
        density={density}
        sort={tableSortOf(state.sort, state.dir, DESC_BY_DEFAULT)}
        onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
        columns={columns}
        rows={tasks}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('tasks.page.empty')}
        emptyAction={canCreate ? <Button onClick={() => setShowNewDialog(true)}>{t('tasks.page.newTask')}</Button> : undefined}
        onRowClick={(row) => openDetail(row.id)}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={total} onPageChange={(next) => apply({ page: next })} />

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
