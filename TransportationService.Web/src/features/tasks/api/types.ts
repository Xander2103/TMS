import type { BadgeTone } from '../../../components/ui/Badge'

export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Urgent'

export type TaskStatus = 'Todo' | 'InProgress' | 'Blocked' | 'WaitingForReview' | 'Completed' | 'Cancelled'

export type TaskRecurrenceInterval = 'Daily' | 'Weekly' | 'Monthly' | 'Yearly' | 'CustomDays'

/** Mirrors EmployeeTaskDto (camelCase JSON). */
export interface EmployeeTask {
  id: string
  title: string
  description: string | null
  categoryId: string | null
  categoryName: string | null
  categoryColor: string | null
  priority: TaskPriority
  status: TaskStatus
  startAt: string | null
  dueAt: string | null
  completedAt: string | null
  cancelledAt: string | null
  assignedEmployeeId: string
  assignedEmployeeName: string
  createdByUserId: string | null
  createdByName: string | null
  requiresReview: boolean
  requiresCompletionNote: boolean
  requiresEvidence: boolean
  relatedEntityType: string | null
  relatedEntityId: string | null
  blockedReason: string | null
  completionNote: string | null
  reviewNote: string | null
  reviewedByUserId: string | null
  reviewedAt: string | null
  reopenedAt: string | null
  isOverdue: boolean
  /** Optimistic-concurrency token; every status action sends it as expectedVersion. */
  version: number
  createdAt: string
  updatedAt: string
  batchId: string | null
  recurrenceId: string | null
}

/** Mirrors TaskListResultDto — note `total`, not the shared PagedResult's `totalCount`. */
export interface TaskListResult {
  items: EmployeeTask[]
  total: number
  page: number
  pageSize: number
}

export interface TaskListParams {
  mine?: boolean
  createdByMe?: boolean
  employeeId?: string
  departmentId?: string
  categoryId?: string
  status?: TaskStatus
  priority?: TaskPriority
  dueBefore?: string
  overdueOnly?: boolean
  waitingForReviewOnly?: boolean
  search?: string
  page?: number
  pageSize?: number
}

export interface CreateTaskInput {
  title: string
  assignedEmployeeIds: string[]
  description?: string
  categoryId?: string
  priority?: TaskPriority
  startAt?: string
  dueAt?: string
  requiresReview?: boolean
  requiresCompletionNote?: boolean
  requiresEvidence?: boolean
}

export interface UpdateTaskInput {
  title: string
  description: string | null
  categoryId: string | null
  priority: TaskPriority
  startAt: string | null
  dueAt: string | null
  requiresReview: boolean
  requiresCompletionNote: boolean
  requiresEvidence: boolean
  expectedVersion: number
}

export interface TaskOpenSummary {
  employeeId: string
  todo: number
  inProgress: number
  blocked: number
  waitingForReview: number
  overdue: number
}

export interface RedistributeTasksInput {
  fromEmployeeId: string
  action: 'reassign' | 'cancel'
  targetEmployeeId?: string
  newDueAt?: string
  reason?: string
}

export interface RedistributeResult {
  affectedTasks: number
  action: string
}

/** Mirrors TaskAttachmentDto. */
export interface TaskAttachment {
  id: string
  taskId: string
  fileName: string
  contentType: string
  sizeBytes: number
  note: string | null
  uploadedByUserId: string | null
  createdAt: string
}

export interface TaskTemplateItem {
  id: string
  sortOrder: number
  title: string
  description: string | null
  categoryId: string | null
  priority: TaskPriority
  dueInDays: number | null
  requiresReview: boolean
  requiresCompletionNote: boolean
  requiresEvidence: boolean
}

export interface TaskTemplate {
  id: string
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
  items: TaskTemplateItem[]
}

export interface SaveTaskTemplateItemInput {
  title: string
  description: string | null
  categoryId: string | null
  priority: TaskPriority
  dueInDays: number | null
  requiresReview: boolean
  requiresCompletionNote: boolean
  requiresEvidence: boolean
}

export interface SaveTaskTemplateInput {
  name: string
  description: string | null
  isActive: boolean
  sortOrder: number
  items: SaveTaskTemplateItemInput[]
}

export interface TaskRecurrence {
  id: string
  templateId: string
  templateName: string
  assignedEmployeeId: string
  assignedEmployeeName: string
  interval: TaskRecurrenceInterval
  customIntervalDays: number | null
  /** yyyy-MM-dd */
  startDate: string
  endDate: string | null
  isActive: boolean
  lastGeneratedPeriod: string | null
}

export interface SaveTaskRecurrenceInput {
  templateId: string
  assignedEmployeeId: string
  interval: TaskRecurrenceInterval
  customIntervalDays: number | null
  startDate: string
  endDate: string | null
  isActive: boolean
}

// ---------------------------------------------------------- label keys & tones

/** Translation keys per status; render via t(TASK_STATUS_LABELS[status]). */
export const TASK_STATUS_LABELS: Record<TaskStatus, string> = {
  Todo: 'tasks.status.Todo',
  InProgress: 'tasks.status.InProgress',
  Blocked: 'tasks.status.Blocked',
  WaitingForReview: 'tasks.status.WaitingForReview',
  Completed: 'tasks.status.Completed',
  Cancelled: 'tasks.status.Cancelled',
}

export const TASK_STATUS_TONES: Record<TaskStatus, BadgeTone> = {
  Todo: 'neutral',
  InProgress: 'info',
  Blocked: 'danger',
  WaitingForReview: 'warning',
  Completed: 'success',
  Cancelled: 'neutral',
}

/** Translation keys per priority; render via t(TASK_PRIORITY_LABELS[priority]). */
export const TASK_PRIORITY_LABELS: Record<TaskPriority, string> = {
  Low: 'tasks.priority.Low',
  Normal: 'tasks.priority.Normal',
  High: 'tasks.priority.High',
  Urgent: 'tasks.priority.Urgent',
}

export const TASK_PRIORITY_TONES: Record<TaskPriority, BadgeTone> = {
  Low: 'neutral',
  Normal: 'info',
  High: 'warning',
  Urgent: 'danger',
}

/** Translation keys per interval; render via t(TASK_RECURRENCE_INTERVAL_LABELS[interval]). */
export const TASK_RECURRENCE_INTERVAL_LABELS: Record<TaskRecurrenceInterval, string> = {
  Daily: 'tasks.interval.Daily',
  Weekly: 'tasks.interval.Weekly',
  Monthly: 'tasks.interval.Monthly',
  Yearly: 'tasks.interval.Yearly',
  CustomDays: 'tasks.interval.CustomDays',
}

/** Statuses that count as "open" (mirrors TaskStatusMachine.OpenStatuses). */
export const OPEN_TASK_STATUSES: TaskStatus[] = ['Todo', 'InProgress', 'Blocked', 'WaitingForReview']

/** Allowed evidence upload types (pdf/jpg/png, max 10 MB server-side). */
export const TASK_ATTACHMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png'
