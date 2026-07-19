import { describe, expect, it } from 'vitest'
import {
  SCHEDULE_STATES,
  SCHEDULE_STATE_ICONS,
  SCHEDULE_STATE_LABELS,
  chipDescription,
  formatMinutes,
  mondayOf,
  toIsoDate,
  type ScheduleEntry,
} from '../types'

describe('schedule state metadata', () => {
  it('has a label and non-colour icon for every state (legend completeness)', () => {
    expect(SCHEDULE_STATES).toHaveLength(12)
    for (const state of SCHEDULE_STATES) {
      expect(SCHEDULE_STATE_LABELS[state], `label ${state}`).toBeTruthy()
      expect(SCHEDULE_STATE_ICONS[state], `icon ${state}`).toBeTruthy()
    }
  })

  it('describes a trip chip with number, status, time, route and vehicle', () => {
    const entry: ScheduleEntry = {
      state: 'Trip',
      shiftId: null,
      absenceId: null,
      tripId: 'abc',
      sourceType: 'Trip',
      label: 'RIT-0007',
      startTime: '08:00:00',
      endTime: '16:00:00',
      shiftType: null,
      workLocation: 'Antwerpen → Rotterdam',
      vehicleSummary: 'V-0001 · 1-ABC-123',
      statusLabel: 'Gepland',
      conflictSeverity: null,
      conflictNotes: null,
    }
    expect(chipDescription(entry)).toBe(
      'Rit RIT-0007 · Gepland · 08:00–16:00 · Antwerpen → Rotterdam · V-0001 · 1-ABC-123',
    )
    expect(chipDescription({ ...entry, conflictSeverity: 'Blocking', conflictNotes: ['Overlapt met rit RIT-0008'] })).toContain(
      'Conflict (Blokkerend): Overlapt met rit RIT-0008',
    )
  })
})

describe('planning helpers', () => {
  it('formats minutes as hours', () => {
    expect(formatMinutes(480)).toBe('8u')
    expect(formatMinutes(435)).toBe('7u15')
  })

  it('finds the monday of any date', () => {
    // 2026-07-22 is a Wednesday; its week starts Monday 2026-07-20.
    expect(toIsoDate(mondayOf(new Date('2026-07-22T10:00:00')))).toBe('2026-07-20')
    expect(toIsoDate(mondayOf(new Date('2026-07-20T00:30:00')))).toBe('2026-07-20')
    expect(toIsoDate(mondayOf(new Date('2026-07-26T23:00:00')))).toBe('2026-07-20')
  })
})
