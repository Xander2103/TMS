import { describe, expect, it } from 'vitest'
import {
  STOP_EXECUTION_ICONS,
  STOP_EXECUTION_LABELS,
  STOP_EXECUTION_TONE,
  STOP_PRIMARY_ACTION_ORDER,
  STOP_REASON_REQUIRED,
  STOP_TRANSITION_ACTION_LABELS,
  effectiveLatestBound,
  type ExecutionStop,
  type StopExecutionStatus,
} from '../types'

const ALL_STATUSES: StopExecutionStatus[] = [
  'Planned',
  'EnRoute',
  'Arrived',
  'Loading',
  'Loaded',
  'Unloading',
  'Completed',
  'PartiallyCompleted',
  'Failed',
  'Skipped',
]

describe('stop execution status metadata', () => {
  it('has a label, tone, icon and action label for every status', () => {
    for (const status of ALL_STATUSES) {
      expect(STOP_EXECUTION_LABELS[status], `label ${status}`).toBeTruthy()
      expect(STOP_EXECUTION_TONE[status], `tone ${status}`).toBeTruthy()
      expect(STOP_EXECUTION_ICONS[status], `icon ${status}`).toBeTruthy()
      expect(STOP_TRANSITION_ACTION_LABELS[status], `action ${status}`).toBeTruthy()
    }
  })

  it('demands a reason exactly for the exception statuses', () => {
    expect(STOP_REASON_REQUIRED.toSorted()).toEqual(['Failed', 'PartiallyCompleted', 'Skipped'].toSorted())
  })

  it('offers the forward chain as primary actions ending in Completed', () => {
    expect(STOP_PRIMARY_ACTION_ORDER.at(-1)).toBe('Completed')
    expect(STOP_PRIMARY_ACTION_ORDER).not.toContain('Skipped')
    expect(STOP_PRIMARY_ACTION_ORDER).not.toContain('Failed')
  })
})

describe('effectiveLatestBound', () => {
  const base: ExecutionStop = {
    transportOrderStopId: 's1',
    transportOrderId: 'o1',
    orderNumber: 'ORD-1',
    customerName: 'Klant',
    orderSequence: 1,
    stopSequence: 1,
    stopType: 'Loading',
    locationName: 'Terminal',
    address: null,
    postalCode: null,
    city: null,
    plannedFrom: null,
    plannedTo: '2026-07-21T10:00:00Z',
    requestedFrom: null,
    requestedTo: null,
    confirmedFrom: null,
    confirmedTo: null,
    earliestAllowed: null,
    latestAllowed: null,
    appointmentRequired: false,
    appointmentReference: null,
    instructions: null,
    accessInstructions: null,
    loadingInstructions: null,
    unloadingInstructions: null,
    status: 'Planned',
    arrivedAt: null,
    departedAt: null,
    completedAt: null,
    waitingMinutes: null,
    lateArrivalReason: null,
    statusReason: null,
    allowedTransitions: [],
    hasPod: false,
    podSignedBy: null,
    remarks: null,
  }

  it('prefers latestAllowed over confirmedTo over plannedTo (mirrors the backend)', () => {
    expect(effectiveLatestBound(base)).toBe('2026-07-21T10:00:00Z')
    expect(effectiveLatestBound({ ...base, confirmedTo: '2026-07-21T09:00:00Z' })).toBe('2026-07-21T09:00:00Z')
    expect(
      effectiveLatestBound({ ...base, confirmedTo: '2026-07-21T09:00:00Z', latestAllowed: '2026-07-21T11:00:00Z' }),
    ).toBe('2026-07-21T11:00:00Z')
    expect(effectiveLatestBound({ ...base, plannedTo: null })).toBeNull()
  })
})
