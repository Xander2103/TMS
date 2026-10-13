import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/timeAttendanceApi'
import type { AttendanceOverview } from '../../types'
import { AttendanceOverviewPage } from '../AttendanceOverviewPage'

vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [{ id: 'dept-1', code: 'MAG', name: 'Magazijn' }],
    error: null,
    isLoading: false,
    reload: vi.fn(),
  }),
}))

const overview: AttendanceOverview = {
  date: '2026-08-20',
  summary: { working: 1, onBreak: 1, clockedOut: 0, notClockedIn: 1, plannedNotClockedIn: 1, forgottenClockOut: 0, absent: 1 },
  rows: [
    {
      employeeId: 'e1', name: 'Jan Peeters', employeeNumber: 'E-1', departmentName: 'Magazijn',
      status: 'Working', since: '2026-08-20T05:52:00Z', workedMinutes: 431, breakMinutes: 31,
      plannedMinutes: 450, plannedStart: '08:00:00', plannedEnd: '16:00:00',
      plannedNotClockedIn: false, hasCorrections: false, absenceLabel: null,
    },
    {
      employeeId: 'e2', name: 'Sarah Janssens', employeeNumber: 'E-2', departmentName: null,
      status: 'OnBreak', since: '2026-08-20T10:02:00Z', workedMinutes: 245, breakMinutes: 41,
      plannedMinutes: null, plannedStart: null, plannedEnd: null,
      plannedNotClockedIn: false, hasCorrections: true, absenceLabel: null,
    },
    {
      employeeId: 'e3', name: 'Tom Willems', employeeNumber: 'E-3', departmentName: null,
      status: 'NotClockedIn', since: null, workedMinutes: 0, breakMinutes: 0,
      plannedMinutes: 480, plannedStart: '08:00:00', plannedEnd: '16:00:00',
      plannedNotClockedIn: true, hasCorrections: false, absenceLabel: null,
    },
    {
      employeeId: 'e4', name: 'Els Vermeer', employeeNumber: 'E-4', departmentName: null,
      status: 'Absent', since: null, workedMinutes: 0, breakMinutes: 0,
      plannedMinutes: null, plannedStart: null, plannedEnd: null,
      plannedNotClockedIn: false, hasCorrections: false, absenceLabel: 'Verlof',
    },
  ],
}

describe('AttendanceOverviewPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(api, 'getAttendanceOverview').mockResolvedValue(overview)
  })

  function renderPage() {
    return render(
      <MemoryRouter initialEntries={['/attendance']}>
        <AttendanceOverviewPage />
      </MemoryRouter>,
    )
  }

  it('toont per medewerker status, sinds, gewerkt en pauze met de juiste badges', async () => {
    renderPage()

    const jan = (await screen.findByText('Jan Peeters')).closest('tr')!
    expect(within(jan).getByText('Aan het werk')).toBeInTheDocument()
    expect(within(jan).getByText('07:52')).toBeInTheDocument()
    expect(within(jan).getByText('7u11')).toBeInTheDocument()
    expect(within(jan).getByText('0u31')).toBeInTheDocument()
    expect(within(jan).getByText('08:00 – 16:00')).toBeInTheDocument()

    const tom = screen.getByText('Tom Willems').closest('tr')!
    expect(within(tom).getByText('Gepland, niet ingepunt')).toBeInTheDocument()

    const els = screen.getByText('Els Vermeer').closest('tr')!
    expect(within(els).getByText('Verlof')).toBeInTheDocument()
  })

  it('toont de samenvattingstellers en filtert client-side op status', async () => {
    renderPage()
    await screen.findByText('Jan Peeters')

    expect(screen.getByText('Gepland, niet ingepunt', { selector: '.ta-summary-label' })).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'OnBreak')

    expect(screen.queryByText('Jan Peeters')).not.toBeInTheDocument()
    expect(screen.getByText('Sarah Janssens')).toBeInTheDocument()
  })

  it('geeft de zoekterm en afdeling door aan de API', async () => {
    const spy = vi.spyOn(api, 'getAttendanceOverview')
    renderPage()
    await screen.findByText('Jan Peeters')

    await userEvent.selectOptions(screen.getByLabelText('Afdeling'), 'dept-1')

    expect(spy).toHaveBeenLastCalledWith(expect.objectContaining({ departmentId: 'dept-1' }))
  })
})
