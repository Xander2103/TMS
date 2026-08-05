import { describe, expect, it } from 'vitest'
import type { LocationOpeningInterval } from '../../../locations/types'
import { checkOpeningHours, isoDayOfWeek, openingHoursWarning } from '../openingHours'

const monday: LocationOpeningInterval[] = [
  { dayOfWeek: 1, fromTime: '08:00', toTime: '12:00', note: null },
  { dayOfWeek: 1, fromTime: '13:00', toTime: '17:00', note: null },
]

describe('isoDayOfWeek', () => {
  it('maps Monday to 1 and Sunday to 7', () => {
    expect(isoDayOfWeek('2026-07-20')).toBe(1) // maandag
    expect(isoDayOfWeek('2026-07-19')).toBe(7) // zondag
  })

  it('returns null for garbage', () => {
    expect(isoDayOfWeek('niet-een-datum')).toBeNull()
  })
})

describe('checkOpeningHours', () => {
  it('reports noData without intervals', () => {
    expect(checkOpeningHours([], 1, '09:00').verdict).toBe('noData')
    expect(checkOpeningHours(null, 1, '09:00').verdict).toBe('noData')
  })

  it('reports inside within a window (start inclusive, end exclusive)', () => {
    expect(checkOpeningHours(monday, 1, '08:00').verdict).toBe('inside')
    expect(checkOpeningHours(monday, 1, '16:59').verdict).toBe('inside')
    expect(checkOpeningHours(monday, 1, '17:00').verdict).toBe('outside')
  })

  it('reports outside before opening, in the gap and after closing', () => {
    expect(checkOpeningHours(monday, 1, '07:30').verdict).toBe('outside')
    expect(checkOpeningHours(monday, 1, '12:30').verdict).toBe('outside')
    expect(checkOpeningHours(monday, 1, '18:30').verdict).toBe('outside')
  })

  it('reports closedDay when the day has no intervals', () => {
    expect(checkOpeningHours(monday, 7, '10:00').verdict).toBe('closedDay')
  })

  it('lists the full day hours for the warning text', () => {
    expect(checkOpeningHours(monday, 1, '12:30').dayHours).toBe('08:00–12:00, 13:00–17:00')
  })
})

describe('openingHoursWarning', () => {
  it('builds the Dutch outside-hours line', () => {
    expect(openingHoursWarning(monday, '2026-07-20', '18:30', 'lostijd', 'Magazijn Antwerpen')).toBe(
      'De geplande lostijd van 18:30 valt buiten de openingsuren (08:00–12:00, 13:00–17:00) van Magazijn Antwerpen.',
    )
  })

  it('builds the Dutch closed-day line', () => {
    expect(openingHoursWarning(monday, '2026-07-19', '10:00', 'laadtijd', 'Magazijn Antwerpen')).toBe(
      'De geplande laadtijd op zondag valt op een sluitingsdag van Magazijn Antwerpen.',
    )
  })

  it('stays silent inside hours and without data', () => {
    expect(openingHoursWarning(monday, '2026-07-20', '09:00', 'laadtijd', 'X')).toBeNull()
    expect(openingHoursWarning([], '2026-07-20', '03:00', 'laadtijd', 'X')).toBeNull()
  })
})
