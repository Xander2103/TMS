import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { AbsencesPage } from '../pages/AbsencesPage'
import type { Absence } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }),
}))

const api = vi.hoisted(() => ({
  searchAbsences: vi.fn(),
  cancelAbsence: vi.fn(),
  deleteAbsence: vi.fn(),
  getAbsenceReviewContext: vi.fn(),
}))

vi.mock('../api/absencesApi', () => ({
  searchAbsences: (...args: unknown[]) => api.searchAbsences(...args),
  cancelAbsence: (...args: unknown[]) => api.cancelAbsence(...args),
  deleteAbsence: (...args: unknown[]) => api.deleteAbsence(...args),
  getAbsenceReviewContext: (...args: unknown[]) => api.getAbsenceReviewContext(...args),
  decideAbsence: vi.fn(),
  requestAbsenceChanges: vi.fn(),
  setAbsenceInternalNote: vi.fn(),
  startAbsenceReview: vi.fn(),
}))

function absence(overrides: Partial<Absence>): Absence {
  return {
    id: 'a-1',
    employeeId: 'e-1',
    employeeName: 'Jan Peeters',
    employeeNumber: 'P-001',
    isDriver: false,
    type: 'Vacation',
    startDate: '2026-10-20',
    endDate: '2026-10-24',
    status: 'Requested',
    reason: 'Herfstvakantie',
    decisionNote: null,
    decidedAt: null,
    partDay: 'FullDay',
    internalNote: null,
    hasAttachment: false,
    attachmentFileName: null,
    leaveTypeId: null,
    ...overrides,
  }
}

function renderAt(url = '/absences') {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <AbsencesPage />
    </MemoryRouter>,
  )
}

describe('AbsencesPage (B50)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getAbsenceReviewContext.mockReturnValue(new Promise(() => {}))
    api.searchAbsences.mockResolvedValue({
      items: [
        absence({ id: 'a-1', employeeName: 'Jan Peeters', status: 'Requested' }),
        absence({ id: 'a-2', employeeName: 'Els Maes', status: 'Approved' }),
      ],
      totalCount: 2,
      page: 1,
      pageSize: 25,
    })
  })

  afterEach(cleanup)

  it('loads the paged server endpoint with the view from the URL', async () => {
    renderAt('/absences?search=jan&status=Requested&sort=employee&dir=desc&page=2&from=2026-11-01&to=2026-11-30')
    await waitFor(() => expect(api.searchAbsences).toHaveBeenCalled())
    expect(api.searchAbsences.mock.calls[0][0]).toMatchObject({
      search: 'jan',
      status: 'Requested',
      sort: 'employee',
      dir: 'desc',
      page: 2,
      from: '2026-11-01',
      to: '2026-11-30',
    })
  })

  it('ignores an unknown status and a malformed date in the URL', async () => {
    renderAt('/absences?status=Bogus&from=gisteren')
    await waitFor(() => expect(api.searchAbsences).toHaveBeenCalled())
    const params = api.searchAbsences.mock.calls[0][0] as Record<string, unknown>
    expect(params.status).toBeUndefined()
    expect(params.from).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('shows "Beoordelen" only on a pending request; other actions sit in the row menu', async () => {
    renderAt()
    const pendingRow = (await screen.findByText('Jan Peeters')).closest('tr')!
    const approvedRow = screen.getByText('Els Maes').closest('tr')!

    expect(within(pendingRow).getByRole('button', { name: 'Beoordelen' })).toBeInTheDocument()
    expect(within(approvedRow).queryByRole('button', { name: 'Beoordelen' })).toBeNull()

    await userEvent.click(within(approvedRow).getByRole('button', { name: 'Acties Els Maes' }))
    const menu = screen.getByRole('menu')
    expect(within(menu).getByRole('menuitem', { name: 'Openen bij medewerker' })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Details en interne notitie' })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Afwezigheid annuleren' })).toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Verwijderen' })).toBeInTheDocument()
  })

  it('opens the review dialog from the direct action', async () => {
    renderAt()
    const pendingRow = (await screen.findByText('Jan Peeters')).closest('tr')!
    await userEvent.click(within(pendingRow).getByRole('button', { name: 'Beoordelen' }))
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(api.getAbsenceReviewContext).toHaveBeenCalledWith('a-1')
  })
})
