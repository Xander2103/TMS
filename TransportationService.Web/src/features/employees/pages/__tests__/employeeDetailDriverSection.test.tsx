import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'

// Integration coverage for the "Chauffeursgegevens" profile section (formerly the standalone
// "chauffeursprofiel" page tab) and the vertical section rail that the employee profile form
// now uses (SectionedForm orientation="left"). EmployeeForm itself is real here — only its
// heavy data dependencies (lookups) and DriverProfilePanel's content are stubbed, following the
// pattern already used by employeeSectionedForm.test.tsx.

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
vi.mock('../../components/EmployeeTripsTab', () => ({ EmployeeTripsTab: () => null }))
vi.mock('../../components/CreateUserAccountDialog', () => ({ CreateUserAccountDialog: () => null }))
vi.mock('../../components/EmployeeHistoryPanel', () => ({ EmployeeHistoryPanel: () => null }))
vi.mock('../../../drivers/components/DriverProfilePanel', () => ({
  DriverProfilePanel: () => <div data-testid="driver-panel">Chauffeur DRV-0001</div>,
}))
vi.mock('../../../issued-items/IssuedItemsTab', () => ({ IssuedItemsTab: () => null }))
vi.mock('../../../leave-balance/components/LeaveBalanceTab', () => ({ LeaveBalanceTab: () => null }))
vi.mock('../../../absences/components/AbsencesTab', () => ({ AbsencesTab: () => null }))
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

describe('EmployeeDetailPage — Chauffeursgegevens section & vertical rail', () => {
  it('shows a "Chauffeursgegevens" section tab in the profile form for a driver, on a vertical rail', async () => {
    renderAt('/employees/emp-1')
    const sectionTablist = screen.getByRole('tablist', { name: 'Formuliersecties' })
    expect(sectionTablist).toHaveAttribute('aria-orientation', 'vertical')

    const driverTab = screen.getByRole('tab', { name: /Chauffeursgegevens/ })
    expect(driverTab).toBeInTheDocument()
    await userEvent.click(driverTab)
    expect(screen.getByTestId('driver-panel')).toBeInTheDocument()
  })

  it('roves section focus with ArrowDown/ArrowUp (vertical keyboard nav)', async () => {
    renderAt('/employees/emp-1')
    const algemeenTab = screen.getByRole('tab', { name: /Algemeen/ })
    algemeenTab.focus()
    await userEvent.keyboard('{ArrowUp}')
    // Algemeen is first; ArrowUp wraps to the last section tab.
    const tabs = screen.getAllByRole('tab').filter((t) => t.closest('.ui-section-nav'))
    expect(tabs[tabs.length - 1]).toHaveFocus()
  })

  it('the legacy ?tab=chauffeursprofiel deep link lands on the profile tab with Chauffeursgegevens active', async () => {
    renderAt('/employees/emp-1?tab=chauffeursprofiel')
    const driverTab = await screen.findByRole('tab', { name: /Chauffeursgegevens/ })
    expect(driverTab).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('driver-panel')).toBeInTheDocument()
  })
})
