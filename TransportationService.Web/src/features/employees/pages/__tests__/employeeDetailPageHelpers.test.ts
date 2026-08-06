import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fullYearsSince } from '../EmployeeDetailPage'

// Task 10 follow-up: dedicated coverage for the seniority/age helper used by both the header
// subtitle ("In dienst sinds … · n jaar") and the read-only profile's age display ("(n j.)").
// "Today" is pinned via fake timers so the tests never depend on the real system clock.

describe('fullYearsSince', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 6)) // "today" = 6 August 2026
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('floors down when this year\'s anniversary has not been reached yet', () => {
    // Anniversary is 10 August; today is 6 August — not yet reached this year.
    expect(fullYearsSince('2020-08-10')).toBe(5)
  })

  it('counts the year once the anniversary date itself arrives', () => {
    // Anniversary is 6 August — today, exactly.
    expect(fullYearsSince('2020-08-06')).toBe(6)
  })

  it('counts the year once the anniversary has passed', () => {
    // Anniversary was 1 August — already passed this year.
    expect(fullYearsSince('2020-08-01')).toBe(6)
  })

  it('returns 0 for a start date less than a year ago', () => {
    expect(fullYearsSince('2026-01-15')).toBe(0)
  })

  it('returns 0 for a start date earlier this same year, before the anniversary concept applies', () => {
    expect(fullYearsSince('2026-08-06')).toBe(0)
  })

  it('returns null when there is no date', () => {
    expect(fullYearsSince(null)).toBeNull()
    expect(fullYearsSince(undefined)).toBeNull()
  })

  it('returns null for an invalid date string', () => {
    expect(fullYearsSince('not-a-date')).toBeNull()
  })
})
