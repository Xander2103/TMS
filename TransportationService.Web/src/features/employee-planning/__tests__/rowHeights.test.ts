import { describe, expect, it } from 'vitest'
import { estimatePlanningRowHeight } from '../rowHeights'
import type { ScheduleEntry, ScheduleRow } from '../types'

function rowWith(entriesPerDay: number): ScheduleRow {
  const entry = { state: 'Planned' } as ScheduleEntry
  return {
    employeeId: 'e', employeeName: 'E', employeeNumber: '1', departmentName: null, plannedMinutes: 0,
    days: [{ date: '2027-03-01', entries: Array.from({ length: entriesPerDay }, () => entry) }],
  } as unknown as ScheduleRow
}

describe('estimatePlanningRowHeight (B49)', () => {
  it('compact roughly halves a typical managed row', () => {
    for (const entries of [1, 2, 3]) {
      const normal = estimatePlanningRowHeight(rowWith(entries), 'normal', true)
      const compact = estimatePlanningRowHeight(rowWith(entries), 'compact', true)
      expect(compact).toBeLessThanOrEqual(normal * 0.6)
    }
  })

  it('reserves no add-shift line in compact', () => {
    expect(estimatePlanningRowHeight(rowWith(1), 'compact', true)).toBe(estimatePlanningRowHeight(rowWith(1), 'compact', false))
  })
})
