import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

// Item 21: an unknown employee id (HTTP 404) is a Not Found state with a way back to the list,
// not a generic load error with "Opnieuw proberen"; a 403 renders the access-denied state.

const auth = vi.hoisted(() => ({ permissions: new Set<string>(), employeeId: null as string | null }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    user: { employeeId: auth.employeeId },
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../components/EmployeeForm', () => ({
  EmployeeForm: () => <div data-testid="employee-form" />,
}))
vi.mock('../../components/QualificationsTab', () => ({ QualificationsTab: () => null }))
vi.mock('../../components/EmployeeDocumentsTab', () => ({ EmployeeDocumentsTab: () => null }))
vi.mock('../../components/EmployeePlanningTab', () => ({ EmployeePlanningTab: () => null }))
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => null }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../components/EmployeeHistoryPanel', () => ({ EmployeeHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({
  DriverProfilePanel: () => <div data-testid="driver-panel">Driver panel</div>,
}))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({
  LeaveBalanceTab: () => <div data-testid="leave-balance">Saldo</div>,
}))
vi.mock('../../../absences/components/AbsencesTab', () => ({
  AbsencesTab: () => <div data-testid="absences">Afwezigheden</div>,
}))
vi.mock('../../components/EmployeeNotesPanel', () => ({ EmployeeNotesPanel: () => null }))
vi.mock('../../../time-attendance/components/AttendanceTab', () => ({
  AttendanceTab: () => <div data-testid="attendance">Uren</div>,
}))
vi.mock('../../../tasks/components/EmployeeTasksTab', () => ({ EmployeeTasksTab: () => null }))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({ isSubmitting: false, error: null, fieldErrors: {}, update: vi.fn() }),
}))
const hook = vi.hoisted(() => ({ errorStatus: 404 as number | null, reload: vi.fn() }))

vi.mock('../../hooks/useEmployee', () => ({
  useEmployee: () => ({
    employee: null,
    isLoading: false,
    error: 'Medewerker kon niet worden geladen.',
    errorStatus: hook.errorStatus,
    reload: hook.reload,
  }),
}))

function renderAt(initialPath: string) {
  const router = createMemoryRouter(
    [{ path: '/employees/:id', element: <EmployeeDetailPage /> }],
    { initialEntries: [initialPath] },
  )
  render(<RouterProvider router={router} />)
}

describe('EmployeeDetailPage load failures', () => {
  it('shows "Medewerker niet gevonden" with a link back to the list on 404, without retry', () => {
    hook.errorStatus = 404
    renderAt('/employees/00000000-0000-0000-0000-000000000001')
    expect(screen.getByRole('alert')).toHaveTextContent('Medewerker niet gevonden')
    expect(screen.getByRole('link', { name: 'Terug naar de lijst' })).toHaveAttribute('href', '/employees')
    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
  })

  it('shows the access-denied state on 403', () => {
    hook.errorStatus = 403
    renderAt('/employees/emp-1')
    expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
  })

  it('keeps the retry for other failures', () => {
    hook.errorStatus = 500
    renderAt('/employees/emp-1')
    expect(screen.getByRole('alert')).toHaveTextContent('Medewerker kon niet worden geladen.')
    expect(screen.getByRole('button', { name: 'Opnieuw proberen' })).toBeInTheDocument()
  })
})
