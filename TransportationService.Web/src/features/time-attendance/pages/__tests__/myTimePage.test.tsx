import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/timeAttendanceApi'
import type { AttendanceHistory, AttendanceStatus } from '../../types'
import { MyTimePage, periodRange } from '../MyTimePage'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

const emptyStatus: AttendanceStatus = {
  status: 'NotClockedIn', sessionId: null, clockInAt: null, breakStartedAt: null, lastClockOutAt: null,
  workedMinutesToday: 0, breakMinutesToday: 0,
  canClockIn: true, canClockOut: false, canStartBreak: false, canEndBreak: false,
}

const history: AttendanceHistory = {
  from: '2026-08-17',
  to: '2026-08-20',
  totalGrossMinutes: 1010,
  totalBreakMinutes: 62,
  totalNetMinutes: 948,
  totalPlannedMinutes: 960,
  days: [
    {
      date: '2026-08-19',
      grossMinutes: 510, breakMinutes: 31, netMinutes: 479, plannedMinutes: 480, deviationMinutes: -1,
      sessions: [{
        id: 's1', employeeId: 'e1', clockInAt: '2026-08-19T05:54:00Z', clockOutAt: '2026-08-19T14:24:00Z',
        status: 'Completed', clockInSource: 'Web', locationId: null, locationName: null,
        grossMinutes: 510, breakMinutes: 31, netMinutes: 479, hasCorrections: false, version: 'v1',
        breaks: [{ id: 'b1', startedAt: '2026-08-19T10:03:00Z', endedAt: '2026-08-19T10:34:00Z', minutes: 31 }],
        corrections: [],
      }],
    },
    {
      date: '2026-08-20',
      grossMinutes: 500, breakMinutes: 31, netMinutes: 469, plannedMinutes: 480, deviationMinutes: -11,
      sessions: [],
    },
  ],
}

describe('MyTimePage', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(api, 'getMyAttendanceStatus').mockResolvedValue(emptyStatus)
    vi.spyOn(api, 'getMyAttendanceHistory').mockResolvedValue(history)
  })

  function renderPage() {
    return render(
      <MemoryRouter initialEntries={['/portal/time']}>
        <MyTimePage />
      </MemoryRouter>,
    )
  }

  it('toont totalen, planningvergelijking en de dagtimeline', async () => {
    renderPage()

    expect(await screen.findByText('15u48')).toBeInTheDocument() // 948 netto
    expect(screen.getByText('1u02')).toBeInTheDocument() // pauze
    expect(screen.getByText('16u')).toBeInTheDocument() // gepland
    expect(screen.getByText('-0u12')).toBeInTheDocument() // afwijking
    expect(screen.getByText('Ingepunt')).toBeInTheDocument()
    expect(screen.getByText('Pauze gestart')).toBeInTheDocument()
  })

  it('herlaadt met de gekozen periode', async () => {
    const spy = vi.spyOn(api, 'getMyAttendanceHistory')
    renderPage()
    await screen.findByText('15u48')

    await userEvent.click(screen.getByRole('button', { name: 'Vandaag' }))

    const today = periodRange('today')
    expect(spy).toHaveBeenLastCalledWith(today.from, today.to)
  })

  it('berekent de weekperiode vanaf maandag', () => {
    const range = periodRange('week', new Date(2026, 7, 20)) // donderdag 20/08/2026
    expect(range.from).toBe('2026-08-17') // maandag
    expect(range.to).toBe('2026-08-20')
  })
})
