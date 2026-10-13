import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/timeAttendanceApi'
import type { DriverDaySummary } from '../../types'
import { DriverActivityCard } from '../DriverActivityCard'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

function summary(overrides?: Partial<DriverDaySummary>): DriverDaySummary {
  return {
    attendance: {
      status: 'Working', sessionId: 's1', clockInAt: '2026-08-20T04:42:00Z', breakStartedAt: null,
      lastClockOutAt: null, workedMinutesToday: 454, breakMinutesToday: 43,
      canClockIn: false, canClockOut: true, canStartBreak: true, canEndBreak: false,
    },
    plannedToday: [{ startTime: '06:30:00', endTime: '15:00:00', breakMinutes: 30, roleLabel: null }],
    tachographConnected: false,
    ...overrides,
  }
}

describe('DriverActivityCard', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('toont attendance-blok, planning en de expliciete niet-gekoppeld-tachograafmelding', async () => {
    vi.spyOn(api, 'getMyDriverDay').mockResolvedValue(summary())
    render(<DriverActivityCard />)

    expect(await screen.findByText('Werkdag')).toBeInTheDocument()
    expect(screen.getByText('7u34')).toBeInTheDocument() // werk
    expect(screen.getByText('0u43')).toBeInTheDocument() // pauze
    expect(screen.getByText('8u17')).toBeInTheDocument() // diensttijd
    expect(screen.getByText(/Gepland vandaag:/)).toHaveTextContent('06:30–15:00')
    // Geen fake data: tachograaf staat er expliciet als niet gekoppeld.
    expect(screen.getByText(/Tachograafdata niet gekoppeld/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pauze starten' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uitpunten' })).toBeInTheDocument()
  })

  it('punt uit via de kaart en ververst daarna het overzicht', async () => {
    const getSpy = vi.spyOn(api, 'getMyDriverDay').mockResolvedValue(summary())
    const clockOutSpy = vi.spyOn(api, 'clockOut').mockResolvedValue(summary().attendance)
    render(<DriverActivityCard />)

    await userEvent.click(await screen.findByRole('button', { name: 'Uitpunten' }))

    expect(clockOutSpy).toHaveBeenCalledTimes(1)
    expect(getSpy.mock.calls.length).toBeGreaterThanOrEqual(2)
  })
})
