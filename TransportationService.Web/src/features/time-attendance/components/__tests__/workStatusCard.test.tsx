import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../../api/apiClient'
import * as api from '../../api/timeAttendanceApi'
import type { AttendanceStatus } from '../../types'
import { WorkStatusCard } from '../WorkStatusCard'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }),
}))

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

function renderCard() {
  return render(
    <MemoryRouter>
      <WorkStatusCard />
    </MemoryRouter>,
  )
}

describe('WorkStatusCard', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('toont "Niet ingepunt" met alleen de inpunt-actie', async () => {
    vi.spyOn(api, 'getMyAttendanceStatus').mockResolvedValue(status({}))
    renderCard()

    expect(await screen.findByText('Niet ingepunt')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Inpunten' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Uitpunten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pauze starten' })).not.toBeInTheDocument()
  })

  it('toont werkstatus met inpuntmoment, totalen en de juiste acties', async () => {
    vi.spyOn(api, 'getMyAttendanceStatus').mockResolvedValue(status({
      status: 'Working',
      clockInAt: '2026-08-20T05:54:00Z',
      workedMinutesToday: 338,
      breakMinutesToday: 31,
      canClockIn: false,
      canClockOut: true,
      canStartBreak: true,
    }))
    renderCard()

    expect(await screen.findByText('Aan het werk')).toBeInTheDocument()
    expect(screen.getByText('5u38')).toBeInTheDocument()
    expect(screen.getByText('0u31')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pauze starten' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uitpunten' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Inpunten' })).not.toBeInTheDocument()
  })

  it('toont pauzestatus met alleen pauze-beëindigen en uitpunten', async () => {
    vi.spyOn(api, 'getMyAttendanceStatus').mockResolvedValue(status({
      status: 'OnBreak',
      breakStartedAt: '2026-08-20T10:04:00Z',
      canClockIn: false,
      canClockOut: true,
      canEndBreak: true,
    }))
    renderCard()

    expect(await screen.findByText('Pauze', { selector: '.ui-badge' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pauze beëindigen' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pauze starten' })).not.toBeInTheDocument()
  })

  it('punt in via de actie en toont daarna de nieuwe status', async () => {
    vi.spyOn(api, 'getMyAttendanceStatus').mockResolvedValue(status({}))
    const clockInSpy = vi.spyOn(api, 'clockIn').mockResolvedValue(status({
      status: 'Working',
      clockInAt: '2026-08-20T05:54:00Z',
      canClockIn: false,
      canClockOut: true,
      canStartBreak: true,
    }))
    renderCard()

    await userEvent.click(await screen.findByRole('button', { name: 'Inpunten' }))

    expect(clockInSpy).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Aan het werk')).toBeInTheDocument()
  })

  it('verbergt zichzelf zonder personeelsdossier (404)', async () => {
    vi.spyOn(api, 'getMyAttendanceStatus').mockRejectedValue(new ApiError('Not found', 404))
    const { container } = renderCard()

    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })
})
