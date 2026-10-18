import { describe, expect, it, vi } from 'vitest'
import { resetTimeZonePreference, setTimeZonePreference, todayLocalIsoDate } from '../../../utils/dates'
import {
  comparePeriods,
  dateToPeriod,
  formatPeriod,
  monthInputToPeriod,
  periodToMonthInput,
} from '../utils/invoicePeriod'

describe('monthInputToPeriod', () => {
  it('parses a valid month-input value', () => {
    expect(monthInputToPeriod('2026-07')).toEqual({ year: 2026, month: 7 })
    expect(monthInputToPeriod('2026-12')).toEqual({ year: 2026, month: 12 })
    expect(monthInputToPeriod('2026-01')).toEqual({ year: 2026, month: 1 })
  })

  it('rejects empty or malformed values', () => {
    expect(monthInputToPeriod('')).toBeNull()
    expect(monthInputToPeriod('2026')).toBeNull()
    expect(monthInputToPeriod('26-07')).toBeNull()
    expect(monthInputToPeriod('2026-7')).toBeNull()
    expect(monthInputToPeriod('2026-07-21')).toBeNull()
    expect(monthInputToPeriod('kapot')).toBeNull()
  })

  it('rejects out-of-range months', () => {
    expect(monthInputToPeriod('2026-00')).toBeNull()
    expect(monthInputToPeriod('2026-13')).toBeNull()
  })
})

describe('periodToMonthInput', () => {
  it('pads the month to two digits', () => {
    expect(periodToMonthInput(2026, 7)).toBe('2026-07')
    expect(periodToMonthInput(2026, 12)).toBe('2026-12')
  })

  it('roundtrips with monthInputToPeriod', () => {
    expect(monthInputToPeriod(periodToMonthInput(2025, 1))).toEqual({ year: 2025, month: 1 })
  })
})

describe('formatPeriod', () => {
  it('formats as MM-YYYY for the Dutch UI', () => {
    expect(formatPeriod(2026, 7)).toBe('07-2026')
    expect(formatPeriod(2026, 11)).toBe('11-2026')
  })
})

describe('dateToPeriod', () => {
  it('takes the month of an ISO date string', () => {
    expect(dateToPeriod('2026-07-21')).toEqual({ year: 2026, month: 7 })
    expect(dateToPeriod('2025-01-01')).toEqual({ year: 2025, month: 1 })
  })

  it('falls back to the current (tenant-local) month for missing or invalid input', () => {
    const [year, month] = todayLocalIsoDate().split('-').map(Number)
    const expected = { year, month }
    expect(dateToPeriod(null)).toEqual(expected)
    expect(dateToPeriod(undefined)).toEqual(expected)
    expect(dateToPeriod('kapot')).toEqual(expected)
  })

  it('MD-M3: uses the tenant time zone, not UTC, right after local midnight on the 1st', () => {
    // 2026-11-01 00:30 in Brussels is still 2026-10-31 23:30 UTC.
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-31T23:30:00Z'))
    setTimeZonePreference('Europe/Brussels')
    try {
      expect(dateToPeriod(null)).toEqual({ year: 2026, month: 11 })
    } finally {
      resetTimeZonePreference()
      vi.useRealTimers()
    }
  })
})

describe('comparePeriods', () => {
  it('orders periods within a year', () => {
    expect(comparePeriods({ year: 2026, month: 6 }, { year: 2026, month: 7 })).toBeLessThan(0)
    expect(comparePeriods({ year: 2026, month: 7 }, { year: 2026, month: 7 })).toBe(0)
    expect(comparePeriods({ year: 2026, month: 8 }, { year: 2026, month: 7 })).toBeGreaterThan(0)
  })

  it('orders periods across year boundaries', () => {
    expect(comparePeriods({ year: 2025, month: 12 }, { year: 2026, month: 1 })).toBeLessThan(0)
    expect(comparePeriods({ year: 2026, month: 1 }, { year: 2025, month: 12 })).toBeGreaterThan(0)
  })
})
