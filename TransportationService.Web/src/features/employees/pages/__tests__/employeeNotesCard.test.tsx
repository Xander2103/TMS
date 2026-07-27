import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
const state = vi.hoisted(() => ({ notes: null as string | null }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
// The profile form and the heavy tabs are irrelevant to the notes card.
vi.mock('../../components/EmployeeForm', () => ({
  EmployeeForm: () => <div data-testid="employee-form" />,
}))
vi.mock('../../components/QualificationsTab', () => ({ QualificationsTab: () => null }))
vi.mock('../../components/EmployeeDocumentsTab', () => ({ EmployeeDocumentsTab: () => null }))
vi.mock('../../components/EmployeePlanningTab', () => ({ EmployeePlanningTab: () => null }))
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => null }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../../absences/components/AbsencesTab', () => ({ AbsencesTab: () => null }))
vi.mock('../../../auditing/components/AuditHistoryPanel', () => ({ AuditHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({ DriverProfilePanel: () => null }))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({ LeaveBalanceTab: () => null }))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({ isSubmitting: false, error: null, fieldErrors: {}, update: vi.fn() }),
}))
vi.mock('../../hooks/useEmployee', () => ({
  useEmployee: () => ({
    employee: {
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
      employmentEndDate: null,
      emergencyContacts: [],
      notes: state.notes,
    },
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/employees/emp-1']}>
      <Routes>
        <Route path="/employees/:id" element={<EmployeeDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('EmployeeDetailPage — Notities card', () => {
  beforeEach(() => {
    auth.permissions = new Set(['employees.edit'])
    state.notes = null
  })

  it('shows the note prominently on the profile tab when one exists', () => {
    state.notes = 'Heeft hoogtevrees — nooit inplannen op kraanwerk.'
    renderPage()

    expect(screen.getByRole('heading', { name: 'Notities' })).toBeInTheDocument()
    expect(screen.getByText('Heeft hoogtevrees — nooit inplannen op kraanwerk.')).toBeInTheDocument()
    // The hint points editors to the form section and the history tab.
    expect(screen.getByText(/tabblad Historiek/)).toBeInTheDocument()
  })

  it('renders no notes card when the employee has no note', () => {
    renderPage()
    expect(screen.queryByRole('heading', { name: 'Notities' })).not.toBeInTheDocument()
  })

  it('also shows the note to read-only users', () => {
    auth.permissions = new Set()
    state.notes = 'Enkel leesbaar.'
    renderPage()

    expect(screen.getByRole('heading', { name: 'Notities' })).toBeInTheDocument()
    expect(screen.getByText('Enkel leesbaar.')).toBeInTheDocument()
  })
})
