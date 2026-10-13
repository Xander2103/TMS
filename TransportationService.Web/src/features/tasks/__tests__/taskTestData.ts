import type { EmployeeTask, TaskOpenSummary } from '../api/types'

/** Builds a complete EmployeeTask with sensible defaults for tests. */
export function makeTask(overrides: Partial<EmployeeTask> = {}): EmployeeTask {
  return {
    id: 'task-1',
    title: 'Testtaak',
    description: null,
    categoryId: null,
    categoryName: null,
    categoryColor: null,
    priority: 'Normal',
    status: 'Todo',
    startAt: null,
    dueAt: null,
    completedAt: null,
    cancelledAt: null,
    assignedEmployeeId: 'emp-1',
    assignedEmployeeName: 'An Peeters',
    createdByUserId: 'user-9',
    createdByName: 'Bram Claes',
    requiresReview: false,
    requiresCompletionNote: false,
    requiresEvidence: false,
    relatedEntityType: null,
    relatedEntityId: null,
    blockedReason: null,
    completionNote: null,
    reviewNote: null,
    reviewedByUserId: null,
    reviewedAt: null,
    reopenedAt: null,
    isOverdue: false,
    version: 3,
    createdAt: '2026-08-01T08:00:00Z',
    updatedAt: '2026-08-01T08:00:00Z',
    batchId: null,
    recurrenceId: null,
    ...overrides,
  }
}

export function makeSummary(overrides: Partial<TaskOpenSummary> = {}): TaskOpenSummary {
  return {
    employeeId: 'emp-1',
    todo: 2,
    inProgress: 1,
    blocked: 0,
    waitingForReview: 1,
    overdue: 1,
    ...overrides,
  }
}
