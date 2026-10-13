import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/timeAttendanceApi'
import type { AttendanceHistory } from '../../types'
import { AttendanceTab } from '../AttendanceTab'

const auth = vi.hoisted(() => ({ permissions: ['attendance.view'] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

const history: AttendanceHistory = {
  from: '2026-08-07',
  to: '2026-08-20',
  totalGrossMinutes: 510,
  totalBreakMinutes: 30,
  totalNetMinutes: 480,
  totalPlannedMinutes: 480,
  days: [
    {
      date: '2026-08-20',
      grossMinutes: 510,
      breakMinutes: 30,
      netMinutes: 480,
      plannedMinutes: 480,
      deviationMinutes: 0,
      sessions: [
        {
          id: 's1', employeeId: 'e1', clockInAt: '2026-08-20T05:54:00Z', clockOutAt: '2026-08-20T14:24:00Z',
          status: 'Completed', clockInSource: 'Kiosk', locationId: null, locationName: 'Magazijn',
          grossMinutes: 510, breakMinutes: 30, netMinutes: 480, hasCorrections: true, version: 'v1',
          breaks: [{ id: 'b1', startedAt: '2026-08-20T10:03:00Z', endedAt: '2026-08-20T10:33:00Z', minutes: 30 }],
          corrections: [{
            id: 'c1', kind: 'ClockOut', breakId: null, oldValue: '2026-08-20T14:00:00Z',
            newValue: '2026-08-20T14:24:00Z', reason: 'vergeten uit te punten',
            correctedByName: 'Thalie Arend', correctedAt: '2026-08-20T15:00:00Z',
          }],
        },
      ],
    },
  ],
}

describe('AttendanceTab', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    auth.permissions = ['attendance.view']
    vi.spyOn(api, 'getEmployeeAttendance').mockResolvedValue(history)
    vi.spyOn(api, 'getAttendanceCredentialStatus').mockResolvedValue({
      hasCredential: false, isActive: false, lastUsedAt: null, lockedUntil: null,
    })
  })

  it('toont de timeline met correctie-annotatie (wie + reden + originele waarde)', async () => {
    render(<AttendanceTab employeeId="e1" />)

    expect(await screen.findByText('Ingepunt')).toBeInTheDocument()
    expect(screen.getByText('Pauze gestart')).toBeInTheDocument()
    expect(screen.getByText('Uitgepunt')).toBeInTheDocument()
    expect(screen.getByText(/gecorrigeerd vanuit 16:00 door Thalie Arend · reden: vergeten uit te punten/)).toBeInTheDocument()
    expect(screen.getByText('Prikklok')).toBeInTheDocument()
  })

  it('verbergt correctie- en PIN-acties zonder de bijhorende permissies', async () => {
    render(<AttendanceTab employeeId="e1" />)
    await screen.findByText('Ingepunt')

    expect(screen.queryByRole('button', { name: 'Corrigeren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Manuele registratie' })).not.toBeInTheDocument()
    expect(screen.queryByText('Prikklokcode')).not.toBeInTheDocument()
  })

  it('eist een reden bij een correctie', async () => {
    auth.permissions = ['attendance.view', 'attendance.correct']
    const correctSpy = vi.spyOn(api, 'correctSession')
    render(<AttendanceTab employeeId="e1" />)

    await userEvent.click(await screen.findByRole('button', { name: 'Corrigeren' }))
    await userEvent.click(screen.getByRole('button', { name: 'Correctie opslaan' }))

    expect(toast.showError).toHaveBeenCalledWith('Een reden is verplicht bij elke correctie.')
    expect(correctSpy).not.toHaveBeenCalled()
  })

  it('toont het PIN-beheerblok met manage_credentials en nooit een bestaande code', async () => {
    auth.permissions = ['attendance.view', 'attendance.manage_credentials']
    render(<AttendanceTab employeeId="e1" />)

    expect(await screen.findByText('Prikklokcode')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Code genereren' })).toBeInTheDocument()
    // Er bestaat geen "toon code"-mogelijkheid: alleen genereren/zetten/intrekken.
    expect(screen.queryByText(/toon/i)).not.toBeInTheDocument()
  })
})
