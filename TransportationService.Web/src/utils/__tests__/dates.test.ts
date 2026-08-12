import { afterEach, describe, expect, it } from 'vitest'
import {
  formatDate, formatDateLong, formatDateTime, formatExample,
  parseDisplayDate, parseIsoDate, resetDateFormatPreferenceForTests, setDateFormatPreference,
} from '../dates'

afterEach(() => resetDateFormatPreferenceForTests())

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

describe('formatDate — tenant preference drives every format', () => {
  it('defaults to the Belgian dd/MM/yyyy without a day-shift, even across timezones', () => {
    // This is the case that breaks if date-only strings are parsed as UTC midnight and then
    // rendered in a timezone behind UTC: the calendar day would shift to 31/12/2025.
    expect(formatDate('2026-01-01')).toBe('01/01/2026')
  })

  it('renders every catalog option correctly', () => {
    setDateFormatPreference('dd/MM/yyyy')
    expect(formatDate('2026-08-12')).toBe('12/08/2026')
    setDateFormatPreference('MM/dd/yyyy')
    expect(formatDate('2026-08-12')).toBe('08/12/2026')
    setDateFormatPreference('yyyy-MM-dd')
    expect(formatDate('2026-08-12')).toBe('2026-08-12')
    setDateFormatPreference('dd-MM-yyyy')
    expect(formatDate('2026-08-12')).toBe('12-08-2026')
  })

  it('ignores unknown preference values (closed catalog)', () => {
    setDateFormatPreference('banana')
    expect(formatDate('2026-08-12')).toBe('12/08/2026')
  })

  it('returns an empty string for null or undefined', () => {
    expect(formatDate(null)).toBe('')
    expect(formatDate(undefined)).toBe('')
  })
})

describe('formatDateTime', () => {
  it('renders the preferred date format plus HH:mm', () => {
    const result = formatDateTime('2026-08-06T14:30:00Z')
    // The hour depends on the runner's timezone; assert shape, not the exact hour.
    expect(result).toMatch(/^\d{2}\/\d{2}\/2026 \d{2}:\d{2}$/)
    setDateFormatPreference('yyyy-MM-dd')
    expect(formatDateTime('2026-08-06T14:30:00Z')).toMatch(/^2026-08-06 \d{2}:\d{2}$/)
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
})

describe('formatExample', () => {
  it('shows the settings-screen example per option', () => {
    expect(formatExample('dd/MM/yyyy')).toBe('12/08/2026')
    expect(formatExample('MM/dd/yyyy')).toBe('08/12/2026')
    expect(formatExample('yyyy-MM-dd')).toBe('2026-08-12')
  })
})

describe('parseDisplayDate — strict, unambiguous input parsing', () => {
  it('interprets 03/04/2026 according to the ACTIVE preference, never a locale guess', () => {
    setDateFormatPreference('dd/MM/yyyy')
    expect(parseDisplayDate('03/04/2026')).toBe('2026-04-03')
    setDateFormatPreference('MM/dd/yyyy')
    expect(parseDisplayDate('03/04/2026')).toBe('2026-03-04')
    setDateFormatPreference('yyyy-MM-dd')
    expect(parseDisplayDate('2026-03-04')).toBe('2026-03-04')
  })

  it('rejects impossible dates instead of rolling them over', () => {
    setDateFormatPreference('dd/MM/yyyy')
    expect(parseDisplayDate('31/02/2026')).toBeNull()
    expect(parseDisplayDate('12/13/2026')).toBeNull()
  })

  it('rejects garbage and requires a 4-digit year in the right position', () => {
    expect(parseDisplayDate('vandaag')).toBeNull()
    expect(parseDisplayDate('12/08/26')).toBeNull()
    expect(parseDisplayDate(null)).toBeNull()
  })
})
