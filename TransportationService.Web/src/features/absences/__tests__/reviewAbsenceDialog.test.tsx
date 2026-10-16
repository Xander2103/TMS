import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ReviewAbsenceDialog } from '../components/ReviewAbsenceDialog'
import type { Absence, AbsenceReviewContext } from '../types'

const api = vi.hoisted(() => ({
  decideAbsence: vi.fn(),
  getAbsenceReviewContext: vi.fn(),
  requestAbsenceChanges: vi.fn(),
  setAbsenceInternalNote: vi.fn(),
  startAbsenceReview: vi.fn(),
}))
vi.mock('../api/absencesApi', () => api)
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const absence: Absence = {
  id: 'a1', employeeId: 'emp-1', employeeName: 'TEST HR Acceptatie', employeeNumber: 'MED-0001', isDriver: true,
  type: 'Vacation', startDate: '2026-10-21', endDate: '2026-10-22', status: 'Requested', reason: null,
  decisionNote: null, decidedAt: null, partDay: 'FullDay', internalNote: null, hasAttachment: false,
  attachmentFileName: null, leaveTypeId: null,
}

function context(shifts: number): AbsenceReviewContext {
  return {
    overlappingShifts: Array.from({ length: shifts }, () => ({ date: '2026-10-21', startTime: '08:00:00', endTime: '16:30:00', workLocation: null })),
    overlappingTrips: [],
    overlappingColleagues: [],
    usedVacationDaysThisYear: 0,
    hasAttachment: false,
  } as AbsenceReviewContext
}

beforeEach(() => {
  vi.clearAllMocks()
  api.decideAbsence.mockImplementation((_id: string, approve: boolean) =>
    Promise.resolve({ ...absence, status: approve ? 'Approved' : 'Rejected' }))
})

function renderDialog(onClose = vi.fn()) {
  render(<MemoryRouter><ReviewAbsenceDialog absence={absence} onClose={onClose} onChanged={vi.fn()} /></MemoryRouter>)
  return onClose
}

// HR acceptance P2-12/P2-17.
describe('ReviewAbsenceDialog', () => {
  it('shows the period and shifts in the product date format, without the placeholder note', async () => {
    api.getAbsenceReviewContext.mockResolvedValue(context(1))
    renderDialog()
    expect(screen.getByText(/21\/10\/2026 t\/m 22\/10\/2026/)).toBeInTheDocument()
    expect(await screen.findByText(/21\/10\/2026 08:00–16:30/)).toBeInTheDocument()
    expect(screen.queryByText(/volgt later/)).not.toBeInTheDocument()
  })

  it('after approving over a planned shift, offers the planning follow-up instead of closing', async () => {
    api.getAbsenceReviewContext.mockResolvedValue(context(1))
    const onClose = renderDialog()
    await screen.findByText(/1 shift/)
    fireEvent.click(screen.getByRole('button', { name: '✓ Goedkeuren' }))

    const link = await screen.findByRole('link', { name: 'Personeelsplanning openen voor deze week' })
    expect(link).toHaveAttribute('href', '/employee-planning?week=2026-10-19&employees=emp-1')
    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes after a decision when nothing is planned in the period', async () => {
    api.getAbsenceReviewContext.mockResolvedValue(context(0))
    const onClose = renderDialog()
    await screen.findByText(/Geen shifts/)
    fireEvent.change(screen.getByLabelText(/Notitie voor de medewerker/), { target: { value: 'Drukke periode' } })
    fireEvent.click(screen.getByRole('button', { name: '✕ Afwijzen' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })
})
