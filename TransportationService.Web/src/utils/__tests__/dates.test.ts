import { describe, expect, it } from 'vitest'
import { formatDate, formatDateLong, formatDateTime, parseIsoDate } from '../dates'

describe('parseIsoDate', () => {
  it('parses a date-only string as a local calendar date (no day-shift)', () => {
    const date = parseIsoDate('2026-01-01')
    expect(date).not.toBeNull()
    expect(date?.getFullYear()).toBe(2026)
    expect(date?.getMonth()).toBe(0)
    expect(date?.getDate()).toBe(1)
  })

  it('returns null for null or undefined', () => {
    expect(parseIsoDate(null)).toBeNull()
    expect(parseIsoDate(undefined)).toBeNull()
  })
})

describe('formatDate', () => {
  it('formats a date-only ISO string as d/m/jjjj without a day-shift, even in timezones ahead of/behind UTC', () => {
    // This is the case that breaks if date-only strings are parsed as UTC midnight and then
    // rendered in a timezone behind UTC (e.g. UTC-5): the calendar day would shift back to
    // 31/12/2025. Parsing as a LOCAL date (see parseIsoDate) avoids that entirely.
    expect(formatDate('2026-01-01')).toBe('1/1/2026')
  })

  it('returns an empty string for null or undefined', () => {
    expect(formatDate(null)).toBe('')
    expect(formatDate(undefined)).toBe('')
  })
})

describe('formatDateTime', () => {
  it('formats a timestamp using nl-BE short date + time style', () => {
    const result = formatDateTime('2026-08-06T14:30:00Z')
    expect(result).not.toBe('')
    // Short dateStyle+timeStyle in nl-BE renders as "d/mm/yyyy, HH:mm"; the exact hour depends
    // on the runner's timezone, so only assert the shape and that the raw ISO string is gone.
    expect(result).toMatch(/^\d{1,2}\/\d{2}\/2026, \d{2}:\d{2}$/)
    expect(result).not.toContain('2026-08-06T14:30:00Z')
  })

  it('returns an empty string for null or undefined', () => {
    expect(formatDateTime(null)).toBe('')
    expect(formatDateTime(undefined)).toBe('')
  })
})

describe('formatDateLong', () => {
  it('formats a date-only ISO string as a full Dutch date', () => {
    expect(formatDateLong('2026-08-06')).toBe('donderdag 6 augustus 2026')
  })

  it('returns an empty string for null or undefined', () => {
    expect(formatDateLong(null)).toBe('')
    expect(formatDateLong(undefined)).toBe('')
  })
})
