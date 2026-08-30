import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import {
  formatDate, formatDateTime, formatTime,
  fromDateTimeLocalInput, fromWireDateTime,
  getTimeZonePreference, resetDateFormatPreferenceForTests, resetTimeZonePreferenceForTests,
  setDateFormatPreference, setTimeZonePreference, toDateTimeLocalInput, toWireDateTime,
} from '../dates'

/**
 * C-03 — THE transport-time convention.
 *
 * Storage/wire is UTC; operational wall-clock times belong to the TENANT zone
 * (TenantSettings.Timezone, default Europe/Amsterdam), never to the browser/OS zone. To prove
 * that independence the whole file runs with the process zone forced to America/New_York — a
 * zone that is neither UTC nor the tenant zone, so a browser-zone regression cannot pass by
 * accident. The original TZ is restored afterwards because Vitest reuses a worker process
 * across test files.
 */
// @types/node is deliberately absent from this browser-only project; the runner is Node, so the
// TZ knob is declared locally instead of pulling the whole Node typing surface into the app.
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

afterEach(() => {
  resetTimeZonePreferenceForTests()
  resetDateFormatPreferenceForTests()
})

describe('the machine zone really is different from the tenant zone', () => {
  it('renders a browser-local hour that would betray a browser-zone regression', () => {
    // 06:00Z is 02:00 in New York and 08:00 in Amsterdam: the two never coincide.
    expect(new Date('2026-07-15T06:00:00Z').getHours()).toBe(2)
    expect(getTimeZonePreference()).toBe('Europe/Amsterdam')
  })
})

describe('setTimeZonePreference / getTimeZonePreference', () => {
  it('defaults to Europe/Amsterdam and accepts a valid IANA zone', () => {
    expect(getTimeZonePreference()).toBe('Europe/Amsterdam')
    setTimeZonePreference('Europe/Lisbon')
    expect(getTimeZonePreference()).toBe('Europe/Lisbon')
  })

  it('ignores empty and invalid values instead of corrupting every timestamp', () => {
    setTimeZonePreference('Mars/Olympus_Mons')
    expect(getTimeZonePreference()).toBe('Europe/Amsterdam')
    setTimeZonePreference('')
    expect(getTimeZonePreference()).toBe('Europe/Amsterdam')
    setTimeZonePreference(null)
    expect(getTimeZonePreference()).toBe('Europe/Amsterdam')
  })
})

describe('toWireDateTime — tenant wall clock → UTC instant', () => {
  it('encodes 08:00 Amsterdam summer time as 06:00Z', () => {
    expect(toWireDateTime('2026-07-15', '08:00')).toBe('2026-07-15T06:00:00Z')
  })

  it('encodes 08:00 Amsterdam winter time as 07:00Z', () => {
    expect(toWireDateTime('2026-01-15', '08:00')).toBe('2026-01-15T07:00:00Z')
  })

  it('defaults a missing time to midnight tenant time (the date-only wire encoding)', () => {
    expect(toWireDateTime('2026-07-15', '')).toBe('2026-07-14T22:00:00Z')
    expect(toWireDateTime('2026-07-15')).toBe('2026-07-14T22:00:00Z')
  })

  it('follows the configured zone, not Amsterdam by hardcoding', () => {
    setTimeZonePreference('America/New_York')
    expect(toWireDateTime('2026-07-15', '08:00')).toBe('2026-07-15T12:00:00Z')
    setTimeZonePreference('UTC')
    expect(toWireDateTime('2026-07-15', '08:00')).toBe('2026-07-15T08:00:00Z')
  })

  it('returns null for a missing or malformed date', () => {
    expect(toWireDateTime('', '08:00')).toBeNull()
    expect(toWireDateTime(null, '08:00')).toBeNull()
    expect(toWireDateTime('15/07/2026', '08:00')).toBeNull()
  })
})

describe('DST boundaries in the tenant zone', () => {
  it('uses +01:00 before and +02:00 after the spring-forward switch (29 March 2026)', () => {
    expect(toWireDateTime('2026-03-29', '01:30')).toBe('2026-03-29T00:30:00Z')
    expect(toWireDateTime('2026-03-29', '03:30')).toBe('2026-03-29T01:30:00Z')
  })

  it('resolves the non-existent 02:30 of the spring-forward day to a real instant', () => {
    // 02:30 never happens in Amsterdam on that day; the encoder must still produce a valid
    // instant (03:30 local) rather than NaN or a silent day-shift.
    expect(toWireDateTime('2026-03-29', '02:30')).toBe('2026-03-29T01:30:00Z')
  })

  it('picks the FIRST occurrence of an ambiguous autumn hour (25 October 2026)', () => {
    // 02:30 exists twice; the earlier (CEST, +02:00) reading is chosen deterministically.
    expect(toWireDateTime('2026-10-25', '02:30')).toBe('2026-10-25T00:30:00Z')
    expect(toWireDateTime('2026-10-25', '04:30')).toBe('2026-10-25T03:30:00Z')
  })

  it('renders both sides of the autumn switch in tenant wall-clock time', () => {
    expect(formatTime('2026-10-25T00:30:00Z')).toBe('02:30')
    expect(formatTime('2026-10-25T01:30:00Z')).toBe('02:30')
    expect(formatTime('2026-10-25T02:30:00Z')).toBe('03:30')
  })
})

describe('fromWireDateTime — UTC instant → tenant wall clock', () => {
  it('decodes 06:00Z as 08:00 on the tenant summer day', () => {
    expect(fromWireDateTime('2026-07-15T06:00:00Z')).toEqual({ date: '2026-07-15', time: '08:00' })
  })

  it('rolls the calendar day forward when the tenant zone is ahead of UTC', () => {
    expect(fromWireDateTime('2026-07-15T22:30:00Z')).toEqual({ date: '2026-07-16', time: '00:30' })
  })

  it('treats an offset-less backend timestamp as UTC (parseIsoDate appends the Z)', () => {
    expect(fromWireDateTime('2026-07-15T06:00:00')).toEqual({ date: '2026-07-15', time: '08:00' })
  })

  it('keeps a date-only value a calendar date at midnight', () => {
    expect(fromWireDateTime('2026-07-15')).toEqual({ date: '2026-07-15', time: '00:00' })
  })

  it('returns null for empty or unparseable input', () => {
    expect(fromWireDateTime(null)).toBeNull()
    expect(fromWireDateTime('')).toBeNull()
    expect(fromWireDateTime('geen datum')).toBeNull()
  })
})

describe('round-trip stability', () => {
  const cases: [string, string][] = [
    ['2026-07-15', '08:00'],
    ['2026-01-15', '08:00'],
    ['2026-03-29', '01:30'],
    ['2026-03-29', '03:30'],
    ['2026-10-25', '04:30'],
    ['2026-12-31', '23:59'],
  ]

  it.each(cases)('%s %s survives wall clock → wire → wall clock unchanged', (date, time) => {
    const wire = toWireDateTime(date, time)
    expect(wire).not.toBeNull()
    expect(fromWireDateTime(wire)).toEqual({ date, time })
  })
})

describe('datetime-local input helpers', () => {
  it('maps an ISO-UTC instant to the tenant-zone input value and back', () => {
    expect(toDateTimeLocalInput('2026-07-15T06:00:00Z')).toBe('2026-07-15T08:00')
    expect(fromDateTimeLocalInput('2026-07-15T08:00')).toBe('2026-07-15T06:00:00Z')
  })

  it('treats empty values as absent', () => {
    expect(toDateTimeLocalInput(null)).toBe('')
    expect(toDateTimeLocalInput('')).toBe('')
    expect(fromDateTimeLocalInput('')).toBeNull()
    expect(fromDateTimeLocalInput(null)).toBeNull()
  })

  it('tolerates a seconds-carrying input value', () => {
    expect(fromDateTimeLocalInput('2026-07-15T08:00:00')).toBe('2026-07-15T06:00:00Z')
  })
})

describe('rendering is tenant-zone bound, never browser-zone bound', () => {
  it('formats the time of a UTC instant in the tenant zone', () => {
    // Browser zone would say 02:00 — the whole point of C-03.
    expect(formatTime('2026-07-15T06:00:00Z')).toBe('08:00')
    expect(formatTime('2026-01-15T07:00:00Z')).toBe('08:00')
  })

  it('formats date + time together in the tenant zone and the tenant date format', () => {
    expect(formatDateTime('2026-07-15T06:00:00Z')).toBe('15/07/2026 08:00')
    setDateFormatPreference('yyyy-MM-dd')
    expect(formatDateTime('2026-07-15T06:00:00Z')).toBe('2026-07-15 08:00')
  })

  it('uses the tenant zone for the calendar day of a timestamp', () => {
    // 22:30Z on the 15th is already 00:30 on the 16th in Amsterdam (and 18:30 on the 15th in
    // New York) — the tenant reading wins.
    expect(formatDate('2026-07-15T22:30:00Z')).toBe('16/07/2026')
  })

  it('keeps date-only values calendar-safe (no zone conversion at all)', () => {
    expect(formatDate('2026-07-15')).toBe('15/07/2026')
    expect(formatDate('2026-01-01')).toBe('01/01/2026')
  })

  it('follows a reconfigured tenant zone', () => {
    setTimeZonePreference('America/New_York')
    expect(formatTime('2026-07-15T06:00:00Z')).toBe('02:00')
    setTimeZonePreference('UTC')
    expect(formatTime('2026-07-15T06:00:00Z')).toBe('06:00')
  })
})
