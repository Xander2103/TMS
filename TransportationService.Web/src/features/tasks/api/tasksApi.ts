import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type {
  CreateTaskInput,
  EmployeeTask,
  RedistributeResult,
  RedistributeTasksInput,
  SaveTaskRecurrenceInput,
  SaveTaskTemplateInput,
  TaskAttachment,
  TaskListParams,
  TaskListResult,
  TaskOpenSummary,
  TaskRecurrence,
  TaskTemplate,
  UpdateTaskInput,
} from './types'

// ------------------------------------------------------------------ tasks

export function listTasks(params: TaskListParams): Promise<TaskListResult> {
  const query = new URLSearchParams()
  if (params.mine) query.set('mine', 'true')
  if (params.createdByMe) query.set('createdByMe', 'true')
  if (params.employeeId) query.set('employeeId', params.employeeId)
  if (params.departmentId) query.set('departmentId', params.departmentId)
  if (params.categoryId) query.set('categoryId', params.categoryId)
  if (params.status) query.set('status', params.status)
  if (params.priority) query.set('priority', params.priority)
  if (params.dueBefore) query.set('dueBefore', params.dueBefore)
  if (params.overdueOnly) query.set('overdueOnly', 'true')
  if (params.waitingForReviewOnly) query.set('waitingForReviewOnly', 'true')
  if (params.search) query.set('search', params.search)
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 25))
  return apiClient.getJson<TaskListResult>(`/api/tasks?${query.toString()}`)
}

export function getTask(id: string): Promise<EmployeeTask> {
  return apiClient.getJson<EmployeeTask>(`/api/tasks/${id}`)
}

/** Creates one task per assigned employee (shared batch server-side). */
export function createTasks(input: CreateTaskInput): Promise<EmployeeTask[]> {
  return apiClient.postJson<EmployeeTask[], CreateTaskInput>('/api/tasks', input)
}

export function updateTask(id: string, input: UpdateTaskInput): Promise<EmployeeTask> {
  return apiClient.putJson<EmployeeTask, UpdateTaskInput>(`/api/tasks/${id}`, input)
}

// ------------------------------------------------------------------ status actions

export interface TaskStatusActionInput {
  expectedVersion: number
  note?: string
}

function statusAction(id: string, action: string, input: TaskStatusActionInput): Promise<EmployeeTask> {
  return apiClient.postJson<EmployeeTask, TaskStatusActionInput>(`/api/tasks/${id}/${action}`, input)
}

export const startTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'start', input)
export const blockTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'block', input)
export const resumeTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'resume', input)
export const submitTaskForReview = (id: string, input: TaskStatusActionInput) => statusAction(id, 'submit-for-review', input)
export const completeTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'complete', input)
export const cancelTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'cancel', input)
export const reopenTask = (id: string, input: TaskStatusActionInput) => statusAction(id, 'reopen', input)

export interface ReviewTaskInput {
  expectedVersion: number
  approve: boolean
  note?: string
}

export function reviewTask(id: string, input: ReviewTaskInput): Promise<EmployeeTask> {
  return apiClient.postJson<EmployeeTask, ReviewTaskInput>(`/api/tasks/${id}/review`, input)
}

// ------------------------------------------------------------------ per-employee

export function listEmployeeTasks(employeeId: string): Promise<EmployeeTask[]> {
  return apiClient.getJson<EmployeeTask[]>(`/api/employees/${employeeId}/tasks`)
}

export function getEmployeeOpenTaskSummary(employeeId: string): Promise<TaskOpenSummary> {
  return apiClient.getJson<TaskOpenSummary>(`/api/employees/${employeeId}/tasks/open-summary`)
}

export function redistributeTasks(input: RedistributeTasksInput): Promise<RedistributeResult> {
  return apiClient.postJson<RedistributeResult, RedistributeTasksInput>('/api/tasks/redistribute', input)
}

// ------------------------------------------------------------------ attachments (evidence)

export function listTaskAttachments(taskId: string): Promise<TaskAttachment[]> {
  return apiClient.getJson<TaskAttachment[]>(`/api/tasks/${taskId}/attachments`)
}

async function readUploadError(response: Response, fallback: string): Promise<never> {
  let message = fallback
  try {
    const data = (await response.json()) as { detail?: string; message?: string }
    message = data.detail ?? data.message ?? message
  } catch {
    // keep fallback
  }
  throw new ApiError(message, response.status)
}

/** Multipart upload falls outside the JSON apiClient; attaches the bearer token manually. */
export async function uploadTaskAttachment(taskId: string, file: File, note?: string): Promise<TaskAttachment> {
  const body = new FormData()
  body.append('file', file)
  if (note) body.append('note', note)
  const response = await fetch(`${apiBaseUrl}/api/tasks/${taskId}/attachments`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body,
  })
  if (!response.ok) return readUploadError(response, 'Uploaden is mislukt.')
  return (await response.json()) as TaskAttachment
}

export async function downloadTaskAttachment(taskId: string, attachmentId: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/tasks/${taskId}/attachments/${attachmentId}/content`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('Bijlage kon niet worden gedownload.', response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export function deleteTaskAttachment(taskId: string, attachmentId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/tasks/${taskId}/attachments/${attachmentId}`)
}

// ------------------------------------------------------------------ templates & recurrences

export function listTaskTemplates(includeInactive = false): Promise<TaskTemplate[]> {
  const query = includeInactive ? '?includeInactive=true' : ''
  return apiClient.getJson<TaskTemplate[]>(`/api/task-templates${query}`)
}

export function createTaskTemplate(input: SaveTaskTemplateInput): Promise<TaskTemplate> {
  return apiClient.postJson<TaskTemplate, SaveTaskTemplateInput>('/api/task-templates', input)
}

export function updateTaskTemplate(id: string, input: SaveTaskTemplateInput): Promise<TaskTemplate> {
  return apiClient.putJson<TaskTemplate, SaveTaskTemplateInput>(`/api/task-templates/${id}`, input)
}

export function deleteTaskTemplate(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/task-templates/${id}`)
}

export interface ApplyTaskTemplateInput {
  employeeId: string
  startAt?: string
}

export function applyTaskTemplate(id: string, input: ApplyTaskTemplateInput): Promise<EmployeeTask[]> {
  return apiClient.postJson<EmployeeTask[], ApplyTaskTemplateInput>(`/api/task-templates/${id}/apply`, input)
}

export function listTaskRecurrences(): Promise<TaskRecurrence[]> {
  return apiClient.getJson<TaskRecurrence[]>('/api/task-recurrences')
}

export function createTaskRecurrence(input: SaveTaskRecurrenceInput): Promise<TaskRecurrence> {
  return apiClient.postJson<TaskRecurrence, SaveTaskRecurrenceInput>('/api/task-recurrences', input)
}

export function updateTaskRecurrence(id: string, input: SaveTaskRecurrenceInput): Promise<TaskRecurrence> {
  return apiClient.putJson<TaskRecurrence, SaveTaskRecurrenceInput>(`/api/task-recurrences/${id}`, input)
}

export function deleteTaskRecurrence(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/task-recurrences/${id}`)
}
