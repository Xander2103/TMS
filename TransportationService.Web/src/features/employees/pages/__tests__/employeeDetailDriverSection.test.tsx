import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

// B15 integration: the read-first overview with the REAL EmployeeForm behind a card's
// [Bewerken] (only that card's fields, no section rail, Annuleren returns to reading), and the
// Chauffeur section (driver profile + trips). Only the form's heavy data dependencies (lookups)
// and DriverProfilePanel's content are stubbed, as in employeeSectionedForm.test.tsx.

const auth = vi.hoisted(() => ({ permissions: new Set(['employees.edit']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.has(c)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../components/QualificationsTab', () => ({ QualificationsTab: () => null }))
vi.mock('../../components/EmployeeDocumentsTab', () => ({ EmployeeDocumentsTab: () => null }))
vi.mock('../../components/EmployeePlanningTab', () => ({ EmployeePlanningTab: () => null }))
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => <div data-testid="trips" /> }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../components/EmployeeHistoryPanel', () => ({ EmployeeHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({
  DriverProfilePanel: () => <div data-testid="driver-panel">Chauffeur DRV-0001</div>,
}))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({ LeaveBalanceTab: () => null }))
vi.mock('../../../absences/components/AbsencesTab', () => ({ AbsencesTab: () => null }))
vi.mock('../../components/EmployeeNotesPanel', () => ({ EmployeeNotesPanel: () => null }))
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
  const router = createMemoryRouter([{ path: '/employees/:id', element: <EmployeeDetailPage /> }], {
    initialEntries: [initialPath],
  })
  render(<RouterProvider router={router} />)
  return router
}

describe('EmployeeDetailPage — card edit & Chauffeur section', () => {
  it('reads first: no form until a card is opened, then only that card\'s fields', async () => {
    auth.permissions = new Set(['employees.edit'])
    renderAt('/employees/emp-1')
    expect(screen.queryByLabelText(/Voornaam/)).not.toBeInTheDocument()

    const contact = screen.getByRole('region', { name: 'Contact & adres' })
    await userEvent.click(within(contact).getByRole('button', { name: 'Bewerken' }))

    expect(within(contact).getByLabelText('E-mail')).toBeInTheDocument()
    expect(within(contact).queryByLabelText(/Voornaam/)).not.toBeInTheDocument()
    expect(screen.queryByRole('tablist', { name: 'Formuliersecties' })).not.toBeInTheDocument()
    // One card at a time: the other cards' Bewerken is disabled while this one is open.
    expect(within(screen.getByRole('region', { name: 'Persoonsgegevens' })).getByRole('button', { name: 'Bewerken' })).toBeDisabled()

    await userEvent.click(within(contact).getByRole('button', { name: 'Annuleren' }))
    expect(within(contact).queryByLabelText('E-mail')).not.toBeInTheDocument()
  })

  it('shows the driver profile and, with planning.view, the trips in the Chauffeur section', async () => {
    auth.permissions = new Set(['employees.edit', 'planning.view'])
    renderAt('/employees/emp-1')
    await userEvent.click(within(screen.getByRole('region', { name: 'Chauffeur' })).getByRole('button', { name: 'Chauffeursgegevens openen' }))
    expect(screen.getByRole('tab', { name: 'Chauffeur' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('driver-panel')).toBeInTheDocument()
    expect(screen.getByTestId('trips')).toBeInTheDocument()
  })

  it('keeps the trips out of the Chauffeur section without planning.view', async () => {
    auth.permissions = new Set(['employees.edit'])
    renderAt('/employees/emp-1?tab=chauffeur')
    expect(await screen.findByTestId('driver-panel')).toBeInTheDocument()
    expect(screen.queryByTestId('trips')).not.toBeInTheDocument()
  })
})
