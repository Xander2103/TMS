import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { KioskPage } from '../KioskPage'
import * as kioskApi from '../../api/kioskApi'
import { resetTimeZonePreference } from '../../../../utils/dates'

/**
 * Wave 1 fix A (A12) — /kiosk is routed outside RequireAuth and outside all three shells, so no
 * DisplayPreferencesProvider ever mounts for it: the shared formatters stayed pinned to the
 * default zone forever while the big wall clock beside them used the DEVICE zone. On the same
 * screen the clock said 14:00 and the punch confirmation said "Ingeklokt om 13:00".
 *
 * The kiosk has no session, so it cannot call the display-preferences endpoint — the tenant zone
 * now rides along on the device ping it already performs, and both halves read the same clock.
 *
 * Three zones are deliberately distinct here: the runner is Asia/Tokyo (UTC+9), the fallback is
 * Europe/Amsterdam (UTC+2 in July) and the tenant is Europe/Lisbon (UTC+1), so neither a
 * browser-zone nor a default-zone render can pass by accident.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'Asia/Tokyo'
  expect(new Date('2026-07-15T06:00:00Z').getHours()).toBe(15)
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const lisbonPing = {
  outcome: 'Success' as const,
  deviceName: 'Prikklok magazijn',
  locationName: 'Lissabon',
  error: null,
  defaultLanguage: 'nl' as const,
  timeZone: 'Europe/Lisbon',
}

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

afterEach(() => {
  vi.useRealTimers()
  resetTimeZonePreference()
})

describe('KioskPage — één klok op het scherm (A12)', () => {
  it('renders the wall clock in the tenant zone from the device ping', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(new Date('2026-07-15T06:00:00Z'))
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(lisbonPing)

    render(<KioskPage />)
    await screen.findByText('Prikklok magazijn')

    expect(screen.getByText('07:00')).toBeInTheDocument() // Europe/Lisbon
    expect(screen.queryByText('15:00')).not.toBeInTheDocument() // the device (Tokyo) clock
    expect(screen.queryByText('08:00')).not.toBeInTheDocument() // the hard-coded fallback zone
  })

  it('confirms a punch in the same zone the clock shows', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(new Date('2026-07-15T06:00:00Z'))
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(lisbonPing)
    vi.spyOn(kioskApi, 'kioskIdentify').mockResolvedValue({
      outcome: 'Success', firstName: 'Jan', preferredLanguage: 'nl', interactionToken: 'tok', error: null,
      status: {
        status: 'Working', sessionId: 'sess-1', clockInAt: '2026-07-15T06:00:00Z',
        lastClockOutAt: null, breakStartedAt: null, breakMinutesToday: 0,
        workedMinutesToday: 0, canClockIn: false, canClockOut: true, canStartBreak: true, canEndBreak: false,
      },
    })

    render(<KioskPage />)
    await screen.findByText('Prikklok magazijn')
    for (const digit of ['1', '2', '3', '4']) {
      screen.getByRole('button', { name: digit }).click()
    }
    screen.getByRole('button', { name: 'Bevestigen' }).click()

    // "Je werkt sinds 07:00" — the same Lisbon clock as the wall clock above it.
    expect(await screen.findByText(/07:00/)).toBeInTheDocument()
  })
})
