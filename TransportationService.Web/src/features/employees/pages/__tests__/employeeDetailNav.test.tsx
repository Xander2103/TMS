import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

// B15: ONE section navigation layer (no second form rail on Overzicht), legacy ?tab= and
// ?section= deep links redirecting to their new home, the merged "Verlof & tijd" section
// (leave balance above absences above time registration, each gated by its own permission) and
// the Wave-1 header hierarchy. The profile form itself is exercised separately — stubbed here.

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
vi.mock('../../components/EmployeeNotesPanel', () => ({ EmployeeNotesPanel: () => null }))
vi.mock('../../../time-attendance/components/AttendanceTab', () => ({
  AttendanceTab: () => <div data-testid="attendance">Uren</div>,
}))
vi.mock('../../../tasks/components/EmployeeTasksTab', () => ({ EmployeeTasksTab: () => null }))
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
  'attendance.view',
  'tasks.view_all',
  'employees.deactivate',
]

function tabLabels() {
  return Array.from(document.querySelectorAll('.ui-tabs .ui-tab')).map((t) => t.textContent)
}

describe('EmployeeDetailPage — one navigation layer', () => {
  it('renders the nine sections, and no second (form) navigation on Overzicht', () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    renderAt('/employees/emp-1')
    expect(tabLabels()).toEqual([
      'Overzicht',
      'Planning',
      'Chauffeur',
      'Kwalificaties',
      'Documenten',
      'Verlof & tijd',
      'Taken',
      'Bedrijfsmiddelen',
      'Historiek',
    ])
    expect(document.querySelectorAll('[role="tablist"]')).toHaveLength(1)
    expect(screen.queryByTestId('employee-form')).not.toBeInTheDocument()
  })

  it('hides permission-gated sections and the Chauffeur section without a driver profile', () => {
    auth.permissions = new Set()
    renderAt('/employees/emp-1')
    expect(tabLabels()).toEqual(['Overzicht', 'Chauffeur', 'Kwalificaties', 'Historiek'])
  })
})

describe('EmployeeDetailPage — header actions (Wave 1 hierarchy)', () => {
  it('has one primary Bewerken; deactivation lives in Meer ▾', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    renderAt('/employees/emp-1')
    const header = document.querySelector('.page-header') as HTMLElement
    expect(within(header).getByRole('button', { name: 'Bewerken' })).toHaveClass('ui-button-primary')
    expect(within(header).queryByRole('button', { name: /inactief/ })).not.toBeInTheDocument()
    await userEvent.click(within(header).getByRole('button', { name: 'Meer' }))
    expect(screen.getByRole('menuitem', { name: 'Medewerker inactief zetten' })).toBeInTheDocument()
  })

  it('header Bewerken opens every block of the record at once', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    renderAt('/employees/emp-1')
    await userEvent.click(within(document.querySelector('.page-header') as HTMLElement).getByRole('button', { name: 'Bewerken' }))
    expect(screen.getByRole('region', { name: 'Gegevens bewerken' })).toBeInTheDocument()
    expect(screen.getByTestId('employee-form')).toBeInTheDocument()
  })

  it('shows neither Bewerken nor Meer ▾ for a read-only viewer', () => {
    auth.permissions = new Set()
    renderAt('/employees/emp-1')
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meer' })).not.toBeInTheDocument()
  })
})

describe('EmployeeDetailPage — legacy deep links', () => {
  it('redirects ?tab=verlofsaldo to the merged verlof section', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=verlofsaldo')
    expect(await screen.findByTestId('leave-balance')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?tab=verlof')
  })

  it('redirects ?tab=afwezigheden to the merged verlof section, keeping other params', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=afwezigheden&absenceId=abs-1')
    expect(await screen.findByTestId('absences')).toBeInTheDocument()
    expect(router.state.location.search).toContain('tab=verlof')
    expect(router.state.location.search).toContain('absenceId=abs-1')
  })

  it('redirects ?tab=uren (Urenregistratie) to Verlof & tijd', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?tab=uren')
    expect(await screen.findByTestId('attendance')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?tab=verlof')
  })

  it.each(['chauffeursprofiel', 'ritten'])('redirects ?tab=%s to the Chauffeur section', async (legacy) => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt(`/employees/emp-1?tab=${legacy}`)
    expect(await screen.findByTestId('driver-panel')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?tab=chauffeur')
  })

  it('opens the Chauffeur section for the old ?section=chauffeursgegevens link', async () => {
    auth.permissions = new Set(ALL_PERMISSIONS)
    const router = renderAt('/employees/emp-1?section=chauffeursgegevens')
    expect(await screen.findByTestId('driver-panel')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?tab=chauffeur')
  })
})

describe('EmployeeDetailPage — Verlof & tijd', () => {
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
    expect(labels).not.toContain('Verlof & tijd')
  })
})
