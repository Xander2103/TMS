import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { formatDate, formatDateTime } from '../formatters'
import { resetTimeZonePreferenceForTests, setTimeZonePreference } from '../../utils/dates'

/**
 * C-03 in the portal's own formatters — the deliberate split: the FORMAT follows the reader's
 * language, the CLOCK follows the carrier's (tenant) zone.
 *
 * The process zone is forced to Asia/Tokyo (UTC+9, ahead of the tenant zone) because that is
 * the direction that breaks date-only values: a `YYYY-MM-DD` string is parsed as browser-local
 * midnight, and reprojecting that instant into a zone BEHIND the browser walks the calendar day
 * backwards. Tokyo is where the missing date-only guard showed up as "14/07/2026 17:00".
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Tokyo'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

afterEach(() => resetTimeZonePreferenceForTests())

describe('portal formatDateTime — tenant clock, reader language', () => {
  it('renders a timestamp in the tenant zone, not the browser zone', () => {
    // 06:00Z is 08:00 in Amsterdam and 15:00 in Tokyo; the carrier reading wins.
    expect(formatDateTime('nl', '2026-07-15T06:00:00Z')).toBe('15/07/2026, 08:00')
  })

  it('keeps a date-only value on its own calendar day (no backwards day-shift)', () => {
    // The regression: with the tenant zone applied to a browser-local midnight Date, a Tokyo
    // reader saw 14/07/2026 17:00 for the 15th.
    expect(formatDateTime('nl', '2026-07-15')).toBe('15/07/2026, 00:00')
  })

  it('applies the same date-only guard as formatDate', () => {
    expect(formatDate('nl', '2026-07-15')).toBe('15/7/2026')
    // A timestamp's calendar day IS tenant-zone bound: 22:30Z is already the 16th in Amsterdam.
    expect(formatDate('nl', '2026-07-15T22:30:00Z')).toBe('16/7/2026')
  })

  it('follows the reconfigured tenant zone in both format and clock', () => {
    setTimeZonePreference('Asia/Tokyo')
    expect(formatDateTime('nl', '2026-07-15T06:00:00Z')).toBe('15/07/2026, 15:00')
    expect(formatDateTime('nl', '2026-07-15')).toBe('15/07/2026, 00:00')
  })

  it('returns the raw value for something unparseable', () => {
    expect(formatDateTime('nl', 'geen datum')).toBe('geen datum')
  })
})
