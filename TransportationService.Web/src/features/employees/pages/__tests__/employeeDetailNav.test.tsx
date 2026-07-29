import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

// Navigation redesign (corrections wave 4, phase 3): 8-tab structure, legacy ?tab= aliases
// redirecting to their new home, and the merged "verlof" tab (leave balance above absences,
// each gated by its own permission). The profile form itself is heavy (lookups, sections) and
// is exercised separately — here it's stubbed so these tests stay focused on tab plumbing.

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
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
      driverId: 'drv-1',
      civilStatus: null,
      dependentChildren: null,
      dimonaNumber: null,
      employmentEndDate: null,
      emergencyContacts: [],
      notes: null,
    },
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

function renderAt(initialPath: string) {
  const router = createMemoryRouter(
    [{ path: '/employees/:id', element: <EmployeeDetailPage /> }],
    { initialEntries: [initialPath] },
  )
  render(<RouterProvider router={router} />)
  return router
}

const ALL_PERMISSIONS = [
  'employees.edit',
  'employee_planning.view',
  'employee_documents.view',
  'issued_items.view',
  'leave_balances.view',
  'absences.view',
  'planning.view',
]

describe('EmployeeDetailPage — tab structure', () => {
  it('renders at most 8 page-level tabs when every permission is granted', () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    renderAt('/employees/emp-1')
    const tabBar = document.querySelector('.ui-tabs')
    const tabs = tabBar?.querySelectorAll('.ui-tab') ?? []
    expect(tabs.length).toBeLessThanOrEqual(8)
    expect(tabs.length).toBe(8)
    const labels = Array.from(tabs).map((t) => t.textContent)
    expect(labels).toContain('Overzicht')
    expect(labels).toContain('Verlof & afwezigheden')
    expect(labels).not.toContain('Chauffeursprofiel')
    expect(labels).not.toContain('Verlofsaldo')
    expect(labels).not.toContain('Afwezigheden')
  })
})

describe('EmployeeDetailPage — legacy ?tab= aliases', () => {
  it('redirects ?tab=verlofsaldo to the merged verlof tab', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=verlofsaldo')
    expect(await screen.findByTestId('leave-balance')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?tab=verlof')
  })

  it('redirects ?tab=afwezigheden to the merged verlof tab, keeping other params', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=afwezigheden&absenceId=abs-1')
    expect(await screen.findByTestId('absences')).toBeInTheDocument()
    expect(router.state.location.search).toContain('tab=verlof')
    expect(router.state.location.search).toContain('absenceId=abs-1')
  })

  it('redirects ?tab=chauffeursprofiel to the profile tab with the chauffeursgegevens section', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=chauffeursprofiel')
    await screen.findByTestId('employee-form')
    expect(router.state.location.search).not.toContain('tab=chauffeursprofiel')
    expect(router.state.location.search).toContain('section=chauffeursgegevens')
  })
})

describe('EmployeeDetailPage — merged verlof tab', () => {
  it('shows the leave balance above absences when both permissions are granted', async () => {
    auth.permissions = new Set(['leave_balances.view', 'absences.view'])
    renderAt('/employees/emp-1?tab=verlof')
    const balance = await screen.findByTestId('leave-balance')
    const absences = await screen.findByTestId('absences')
    // Leave balance must precede absences in document order.
    expect(balance.compareDocumentPosition(absences) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('shows only the leave balance without absences.view', async () => {
    auth.permissions = new Set(['leave_balances.view'])
    renderAt('/employees/emp-1?tab=verlof')
    expect(await screen.findByTestId('leave-balance')).toBeInTheDocument()
    expect(screen.queryByTestId('absences')).not.toBeInTheDocument()
  })

  it('shows only absences without leave_balances.view', async () => {
    auth.permissions = new Set(['absences.view'])
    renderAt('/employees/emp-1?tab=verlof')
    expect(await screen.findByTestId('absences')).toBeInTheDocument()
    expect(screen.queryByTestId('leave-balance')).not.toBeInTheDocument()
  })

  it('hides the verlof tab entirely without either permission', () => {
    auth.permissions = new Set()
    renderAt('/employees/emp-1')
    const tabBar = document.querySelector('.ui-tabs')
    const labels = Array.from(tabBar?.querySelectorAll('.ui-tab') ?? []).map((t) => t.textContent)
    expect(labels).not.toContain('Verlof & afwezigheden')
  })
})
