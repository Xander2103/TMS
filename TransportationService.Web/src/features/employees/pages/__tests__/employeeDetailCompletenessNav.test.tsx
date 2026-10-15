import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'
import type { EmployeeCompleteness } from '../../types/employee'

// B15: CompletenessCard → EmployeeDetailPage wiring — "Aanvullen" on a "dienstverband" gap opens
// the Dienstverband card in edit mode (the URL stays clean), "Bekijken" on a "documenten" gap
// switches to the Documenten section, and a "documenten" gap has no action at all for a viewer
// without employee_documents.view (that section wouldn't exist for them to land on).

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../components/EmployeeForm', () => ({
  EmployeeForm: ({ blocks }: { blocks?: string[] }) => <div data-testid="employee-form">{blocks?.join(',')}</div>,
}))
vi.mock('../../components/QualificationsTab', () => ({ QualificationsTab: () => null }))
vi.mock('../../components/EmployeeDocumentsTab', () => ({ EmployeeDocumentsTab: () => <div data-testid="documents-tab" /> }))
vi.mock('../../components/EmployeePlanningTab', () => ({ EmployeePlanningTab: () => null }))
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => null }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../components/EmployeeHistoryPanel', () => ({ EmployeeHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({
  DriverProfilePanel: () => <div data-testid="driver-panel">Driver panel</div>,
}))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({ LeaveBalanceTab: () => null }))
vi.mock('../../../absences/components/AbsencesTab', () => ({ AbsencesTab: () => null }))
vi.mock('../../components/EmployeeNotesPanel', () => ({ EmployeeNotesPanel: () => null }))
vi.mock('../../hooks/useEmployeeMutations', () => ({
  useEmployeeMutations: () => ({ isSubmitting: false, error: null, fieldErrors: {}, update: vi.fn() }),
}))

const COMPLETENESS: EmployeeCompleteness = {
  percentage: 60,
  isComplete: false,
  missingItems: [
    { code: 'employment_start', label: 'Startdatum', section: 'dienstverband' },
    { code: 'contract_document', label: 'Contractdocument', section: 'documenten' },
  ],
}

// Mutable per-test so the seniority-text tests below can vary employmentStartDate.
const employeeState = vi.hoisted(() => ({ employmentStartDate: null as string | null }))

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
      employmentStartDate: employeeState.employmentStartDate,
      employmentEndDate: null,
      emergencyContacts: [],
      notes: null,
      completeness: COMPLETENESS,
    },
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

function renderAt(initialPath: string) {
  const router = createMemoryRouter([{ path: '/employees/:id', element: <EmployeeDetailPage /> }], {
    initialEntries: [initialPath],
  })
  render(<RouterProvider router={router} />)
  return router
}

describe('EmployeeDetailPage — completeness-kaart navigatie', () => {
  it('"Aanvullen" op een dienstverband-gat opent de Dienstverband-kaart in bewerkmodus', async () => {
    auth.permissions = new Set(['employees.edit', 'employee_documents.view'])
    const router = renderAt('/employees/emp-1')

    expect(screen.getByRole('heading', { name: '2 gegevens ontbreken' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Startdatum: Aanvullen' }))

    expect(screen.getByRole('tab', { name: 'Overzicht' })).toHaveAttribute('aria-selected', 'true')
    const card = screen.getByRole('region', { name: 'Dienstverband' })
    expect(within(card).getByTestId('employee-form')).toHaveTextContent('dienstverband')
    expect(router.state.location.search).toBe('')
  })

  it('"Bekijken" op een documenten-gat schakelt naar de Documenten-sectie', async () => {
    auth.permissions = new Set(['employees.edit', 'employee_documents.view'])
    renderAt('/employees/emp-1')

    await userEvent.click(screen.getByRole('button', { name: 'Contractdocument: Bekijken' }))

    expect(screen.getByRole('tab', { name: 'Documenten' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByTestId('documents-tab')).toBeInTheDocument()
  })

  it('het documenten-gat heeft geen actie zonder employee_documents.view', () => {
    auth.permissions = new Set(['employees.edit'])
    renderAt('/employees/emp-1')

    expect(screen.queryByRole('tab', { name: 'Documenten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Contractdocument/ })).not.toBeInTheDocument()
    expect(screen.getByText('Contractdocument')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Startdatum: Aanvullen' })).toBeInTheDocument()
  })

  it('zonder employees.edit: geen acties, geen Bewerken (alleen-lezen weergave)', () => {
    auth.permissions = new Set(['employee_documents.view'])
    renderAt('/employees/emp-1')

    expect(screen.queryByRole('button', { name: /Aanvullen|Bekijken/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
    const card = screen.getByRole('region', { name: 'Dossiervolledigheid' })
    expect(within(card).getByText('Startdatum')).toBeInTheDocument()
  })

  it('een oude ?section=dienstverband-link opent de kaart en wordt uit de URL gehaald', async () => {
    auth.permissions = new Set(['employees.edit'])
    const router = renderAt('/employees/emp-1?section=dienstverband')
    expect(await within(screen.getByRole('region', { name: 'Dienstverband' })).findByTestId('employee-form')).toBeInTheDocument()
    expect(router.state.location.search).toBe('')
  })
})

describe('EmployeeDetailPage — koptekst anciënniteit', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 6)) // "today" = 6 August 2026
    auth.permissions = new Set(['employees.edit', 'employee_documents.view'])
  })

  afterEach(() => {
    employeeState.employmentStartDate = null
    vi.useRealTimers()
  })

  it('toont "· n jaar" wanneer het dienstverband een jaar of langer loopt', () => {
    employeeState.employmentStartDate = '2020-08-06' // exactly 6 years ago today
    renderAt('/employees/emp-1')
    expect(screen.getByText(/In dienst sinds/)).toBeInTheDocument()
    // Header metadata line (and the Dienstverband card repeats it next to the start date).
    expect(screen.getByText(/In dienst sinds/)).toHaveTextContent('6 jaar')
  })

  it('laat "· n jaar" weg wanneer het dienstverband minder dan een jaar loopt', () => {
    employeeState.employmentStartDate = '2026-01-15' // under a year ago
    renderAt('/employees/emp-1')
    expect(screen.getByText(/In dienst sinds/)).toBeInTheDocument()
    expect(screen.queryByText(/jaar/)).not.toBeInTheDocument()
  })

  it('toont geen anciënniteitstekst zonder startdatum', () => {
    employeeState.employmentStartDate = null
    renderAt('/employees/emp-1')
    expect(screen.queryByText(/In dienst sinds/)).not.toBeInTheDocument()
  })
})
