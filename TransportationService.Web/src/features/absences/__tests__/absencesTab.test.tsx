import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AbsencesTab } from '../components/AbsencesTab'
import type { Absence } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true, hasAnyPermission: () => true }),
}))

const api = vi.hoisted(() => ({ listEmployeeAbsences: vi.fn() }))

vi.mock('../api/absencesApi', () => ({
  listEmployeeAbsences: (...args: unknown[]) => api.listEmployeeAbsences(...args),
  cancelAbsence: vi.fn(),
  createAbsence: vi.fn(),
  decideAbsence: vi.fn(),
  deleteAbsence: vi.fn(),
  updateAbsence: vi.fn(),
  getAbsenceReviewContext: () => new Promise(() => {}),
  requestAbsenceChanges: vi.fn(),
  setAbsenceInternalNote: vi.fn(),
  startAbsenceReview: vi.fn(),
}))

vi.mock('../../leave-balance/api/leaveBalanceApi', () => ({ getLeaveTypes: () => Promise.resolve([]) }))

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
    reason: null,
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

describe('AbsencesTab row actions (B50)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listEmployeeAbsences.mockResolvedValue([
      absence({ id: 'a-1', type: 'Vacation', status: 'Requested' }),
      absence({ id: 'a-2', type: 'Sick', status: 'Approved' }),
    ])
  })

  afterEach(cleanup)

  it('shows one direct action only on a pending request, the rest in the row menu', async () => {
    render(<AbsencesTab employeeId="e-1" />)
    const pendingRow = (await screen.findByText('Verlof')).closest('tr')!
    const approvedRow = screen.getByText('Ziekte').closest('tr')!

    expect(within(pendingRow).getByRole('button', { name: 'Beoordelen' })).toBeInTheDocument()
    expect(within(approvedRow).queryByRole('button', { name: 'Beoordelen' })).toBeNull()
    // No stacked link buttons any more.
    expect(within(pendingRow).queryByRole('button', { name: 'Goedkeuren' })).toBeNull()

    await userEvent.click(within(pendingRow).getByRole('button', { name: /^Acties Verlof/ }))
    const pendingMenu = screen.getByRole('menu')
    expect(within(pendingMenu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'Goedkeuren',
      'Afwijzen',
      'Bewerken',
      'Afwezigheid annuleren',
      'Verwijderen',
    ])
    await userEvent.keyboard('{Escape}')

    await userEvent.click(within(approvedRow).getByRole('button', { name: /^Acties Ziekte/ }))
    expect(within(screen.getByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'Afwezigheid annuleren',
      'Verwijderen',
    ])
  })
})
