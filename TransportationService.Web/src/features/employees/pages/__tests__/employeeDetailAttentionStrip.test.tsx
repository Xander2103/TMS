import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'
import type { EmployeeAttention } from '../../types/employee'

// Integration coverage for the expiry-warning strip on the employee page: it sits under the
// header (before the completeness card), a row click opens the right tab with that row
// highlighted, a direct deep link (F5) highlights immediately, and a tab mutation refreshes
// the strip through GET /api/employees/{id}/attention.

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
const api = vi.hoisted(() => ({ getEmployeeAttention: vi.fn() }))
const tabs = vi.hoisted(() => ({ documentsOnChanged: null as null | (() => void) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../api/employeesApi', () => ({
  getEmployeeAttention: (id: string) => api.getEmployeeAttention(id),
}))
vi.mock('../../components/EmployeeForm', () => ({
  EmployeeForm: () => <div data-testid="employee-form" />,
}))
vi.mock('../../components/QualificationsTab', () => ({
  QualificationsTab: ({ highlightQualificationId }: { highlightQualificationId?: string | null }) => (
    <div data-testid="qualifications-tab" data-highlight={highlightQualificationId ?? ''} />
  ),
}))
vi.mock('../../components/EmployeeDocumentsTab', () => ({
  EmployeeDocumentsTab: ({ highlightDocumentId, onChanged }: { highlightDocumentId?: string | null; onChanged?: () => void }) => {
    tabs.documentsOnChanged = onChanged ?? null
    return <div data-testid="documents-tab" data-highlight={highlightDocumentId ?? ''} />
  },
}))
vi.mock('../../components/EmployeePlanningTab', () => ({ EmployeePlanningTab: () => null }))
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => null }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../components/EmployeeHistoryPanel', () => ({ EmployeeHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({ DriverProfilePanel: () => null }))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({ LeaveBalanceTab: () => null }))
vi.mock('../../../absences/components/AbsencesTab', () => ({ AbsencesTab: () => null }))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({ isSubmitting: false, error: null, fieldErrors: {}, update: vi.fn() }),
}))

const ATTENTION: EmployeeAttention = {
  items: [
    { kind: 'document', id: 'doc-1', label: 'Medisch attest', detail: 'Medisch document', expiryDate: '2026-09-24', daysLeft: 12, state: 'expiring' },
    { kind: 'qualification', id: 'qual-1', label: 'Code 95', detail: null, expiryDate: '2026-10-05', daysLeft: 23, state: 'expiring' },
  ],
  documentsExpiring: 1,
  documentsExpired: 0,
  qualificationsExpiring: 1,
  qualificationsExpired: 0,
  hasItems: true,
}

// One stable object: the page seeds its strip state from `employee.attention` in an effect.
const EMPLOYEE = vi.hoisted(() => ({
  id: 'emp-1',
  employeeNumber: 'EMP-0001',
  firstName: 'Jan',
  lastName: 'Peeters',
  functionNames: [],
  isActive: true,
  employmentStatus: 'Active',
  driverId: null,
  civilStatus: null,
  dependentChildren: null,
  dimonaNumber: null,
  employmentStartDate: null,
  employmentEndDate: null,
  emergencyContacts: [],
  notes: null,
  completeness: { percentage: 80, isComplete: false, missingItems: [{ code: 'x', label: 'Rijksregisternummer', section: 'hr' }] },
  attention: null as EmployeeAttention | null,
}))

vi.mock('../../hooks/useEmployee', () => ({
  useEmployee: () => ({ employee: EMPLOYEE, isLoading: false, error: null, reload: vi.fn() }),
}))

function renderAt(initialPath: string) {
  const router = createMemoryRouter([{ path: '/employees/:id', element: <EmployeeDetailPage /> }], {
    initialEntries: [initialPath],
  })
  render(<RouterProvider router={router} />)
  return router
}

describe('EmployeeDetailPage — aandachtspunten-strip', () => {
  beforeEach(() => {
    auth.permissions = new Set(['employees.edit', 'employee_documents.view'])
    EMPLOYEE.attention = ATTENTION
    api.getEmployeeAttention.mockReset()
    tabs.documentsOnChanged = null
  })

  it('shows the strip under the header, before the completeness card', () => {
    renderAt('/employees/emp-1')

    const strip = screen.getByRole('region', { name: 'Aandachtspunten' })
    expect(strip).toBeInTheDocument()
    expect(screen.getByText('1 document verloopt over 12 dagen')).toBeInTheDocument()
    const completeness = screen.getByText('Dossier 80% compleet')
    // DOM order: strip first so HR sees the warnings before the completeness card.
    expect(strip.compareDocumentPosition(completeness) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('renders no strip when the dossier has no attention items', () => {
    EMPLOYEE.attention = null
    renderAt('/employees/emp-1')
    expect(screen.queryByRole('region', { name: 'Aandachtspunten' })).not.toBeInTheDocument()
  })

  it('a document row opens the Documenten tab with that document highlighted', async () => {
    const router = renderAt('/employees/emp-1')

    await userEvent.click(screen.getByRole('button', { name: 'Medisch attest — vervalt op 24/09/2026' }))

    expect(screen.getByRole('tab', { name: 'Documenten' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByTestId('documents-tab')).toHaveAttribute('data-highlight', 'doc-1')
    const params = new URLSearchParams(router.state.location.search)
    expect(params.get('tab')).toBe('documenten')
    expect(params.get('documentId')).toBe('doc-1')
  })

  it('a qualification row opens the Kwalificaties tab with that row highlighted', async () => {
    renderAt('/employees/emp-1')

    await userEvent.click(screen.getByRole('button', { name: 'Code 95 — vervalt op 05/10/2026' }))

    expect(screen.getByRole('tab', { name: 'Kwalificaties' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByTestId('qualifications-tab')).toHaveAttribute('data-highlight', 'qual-1')
  })

  it('a direct deep link (?tab=documenten&documentId=…) highlights the document immediately', () => {
    renderAt('/employees/emp-1?tab=documenten&documentId=doc-1')

    expect(screen.getByRole('tab', { name: 'Documenten' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('documents-tab')).toHaveAttribute('data-highlight', 'doc-1')
  })

  it('refreshes the strip from GET …/attention after a document mutation', async () => {
    api.getEmployeeAttention.mockResolvedValue({
      ...ATTENTION,
      items: [ATTENTION.items[1]],
      documentsExpiring: 0,
    })
    renderAt('/employees/emp-1?tab=documenten')
    expect(screen.getByText('1 document verloopt over 12 dagen')).toBeInTheDocument()

    tabs.documentsOnChanged?.()

    await waitFor(() => expect(screen.queryByText('1 document verloopt over 12 dagen')).not.toBeInTheDocument())
    expect(api.getEmployeeAttention).toHaveBeenCalledWith('emp-1')
    expect(screen.getByText('1 kwalificatie verloopt over 23 dagen')).toBeInTheDocument()
  })
})
