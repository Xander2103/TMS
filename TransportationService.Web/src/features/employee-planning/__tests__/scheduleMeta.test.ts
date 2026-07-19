import { describe, expect, it } from 'vitest'
import {
  SCHEDULE_STATES,
  SCHEDULE_STATE_ICONS,
  SCHEDULE_STATE_LABELS,
  formatMinutes,
  mondayOf,
  toIsoDate,
} from '../types'

describe('schedule state metadata', () => {
  it('has a label and non-colour icon for every state (legend completeness)', () => {
    expect(SCHEDULE_STATES).toHaveLength(10)
    for (const state of SCHEDULE_STATES) {
      expect(SCHEDULE_STATE_LABELS[state], `label ${state}`).toBeTruthy()
      expect(SCHEDULE_STATE_ICONS[state], `icon ${state}`).toBeTruthy()
    }
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
