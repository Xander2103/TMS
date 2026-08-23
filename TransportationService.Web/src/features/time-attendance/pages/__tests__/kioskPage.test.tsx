import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as kioskApi from '../../api/kioskApi'
import type { AttendanceStatus } from '../../types'
import { KioskPage } from '../KioskPage'

function status(overrides: Partial<AttendanceStatus>): AttendanceStatus {
  return {
    status: 'NotClockedIn',
    sessionId: null,
    clockInAt: null,
    breakStartedAt: null,
    lastClockOutAt: null,
    workedMinutesToday: 0,
    breakMinutesToday: 0,
    canClockIn: true,
    canClockOut: false,
    canStartBreak: false,
    canEndBreak: false,
    ...overrides,
  }
}

const okPing = {
  outcome: 'Success' as const,
  deviceName: 'Prikklok magazijn',
  locationName: 'Duisburg',
  error: null,
  defaultLanguage: 'nl' as const,
}

describe('KioskPage', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('toont het instelscherm zolang er geen provisioning-sleutel is', () => {
    render(<KioskPage />)
    expect(screen.getByText('Prikklok instellen')).toBeInTheDocument()
    expect(screen.getByLabelText('Provisioning-sleutel')).toBeInTheDocument()
  })

  it('registreert het device via de provisioning-sleutel en toont dan het PIN-scherm', async () => {
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(okPing)
    render(<KioskPage />)

    await userEvent.type(screen.getByLabelText('Provisioning-sleutel'), 'abc.def')
    await userEvent.click(screen.getByRole('button', { name: 'Registreren' }))

    expect(await screen.findByText('Voer uw code in')).toBeInTheDocument()
    expect(localStorage.getItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE)).toBe('abc.def')
  })

  it('bouwt de code op via het keypad en toont bij een foute code de generieke melding', async () => {
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(okPing)
    const identify = vi.spyOn(kioskApi, 'kioskIdentify').mockResolvedValue({
      outcome: 'InvalidCode', firstName: null, status: null, interactionToken: null, error: 'Code ongeldig.',
      preferredLanguage: null,
    })
    render(<KioskPage />)

    await screen.findByText('Voer uw code in')
    for (const digit of ['1', '2', '3', '4']) {
      await userEvent.click(screen.getByRole('button', { name: digit }))
    }
    await userEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))

    expect(identify).toHaveBeenCalledWith('device.key', '1234')
    expect(await screen.findByRole('alert')).toHaveTextContent('Code ongeldig.')
    // Privacy: de invoer is gewist na de poging.
    expect(screen.getByLabelText('0 cijfers ingevoerd')).toBeInTheDocument()
  })

  it('toont na een geldige code alleen de geldige acties en na de punch een bevestiging', async () => {
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(okPing)
    vi.spyOn(kioskApi, 'kioskIdentify').mockResolvedValue({
      outcome: 'Success',
      firstName: 'Jan',
      status: status({ status: 'Working', clockInAt: '2026-08-20T05:51:00Z', canClockIn: false, canClockOut: true, canStartBreak: true }),
      interactionToken: 'token-1',
      error: null,
      preferredLanguage: null,
    })
    const punch = vi.spyOn(kioskApi, 'kioskPunch').mockResolvedValue({
      outcome: 'Success', status: null, occurredAt: '2026-08-20T05:54:00Z', error: null,
    })
    render(<KioskPage />)

    await screen.findByText('Voer uw code in')
    await userEvent.click(screen.getByRole('button', { name: '1' }))
    await userEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))

    expect(await screen.findByText('Welkom, Jan')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pauze starten' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uitpunten' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Inpunten' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Uitpunten' }))

    expect(punch).toHaveBeenCalledWith('device.key', 'token-1', 'ClockOut')
    expect(await screen.findByText(/Uitgepunt om/)).toBeInTheDocument()
  })

  it('reset automatisch naar het beginscherm na de bevestiging (privacy)', async () => {
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(okPing)
    vi.spyOn(kioskApi, 'kioskIdentify').mockResolvedValue({
      outcome: 'Success', firstName: 'Jan', status: status({}), interactionToken: 'token-1', error: null,
      preferredLanguage: null,
    })
    vi.spyOn(kioskApi, 'kioskPunch').mockResolvedValue({
      outcome: 'Success', status: null, occurredAt: '2026-08-20T05:54:00Z', error: null,
    })
    render(<KioskPage />)

    await screen.findByText('Voer uw code in')
    await userEvent.click(screen.getByRole('button', { name: '1' }))
    await userEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Inpunten' }))
    await screen.findByText(/Ingepunt om/)

    // Auto-reset na 4 s: geen persoonsgegevens meer zichtbaar.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 4_200))
    })
    await waitFor(() => expect(screen.getByText('Voer uw code in')).toBeInTheDocument())
    expect(screen.queryByText(/Jan/)).not.toBeInTheDocument()
  }, 10_000)

  it('meldt eerlijk dat een punch niet geregistreerd is zonder verbinding', async () => {
    localStorage.setItem(kioskApi.KIOSK_DEVICE_KEY_STORAGE, 'device.key')
    vi.spyOn(kioskApi, 'kioskPing').mockResolvedValue(okPing)
    vi.spyOn(kioskApi, 'kioskIdentify').mockRejectedValue(new kioskApi.KioskUnreachableError())
    render(<KioskPage />)

    await screen.findByText('Voer uw code in')
    await userEvent.click(screen.getByRole('button', { name: '1' }))
    await userEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Geen verbinding met de server/)
  })
})
