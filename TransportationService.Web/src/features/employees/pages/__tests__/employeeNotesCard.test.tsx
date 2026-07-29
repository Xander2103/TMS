import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { EmployeeDetailPage } from '../EmployeeDetailPage'
import type { EmployeeNote } from '../../api/employeeNotesApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
const state = vi.hoisted(() => ({ notes: [] as EmployeeNote[] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
// The profile form and the heavy tabs are irrelevant to the notes card; the edit-mode notes
// panel lives inside EmployeeForm's own "Notities" section, covered by employeeNotesPanel.test.tsx.
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
vi.mock('../../api/employeeNotesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/employeeNotesApi')>()),
  listEmployeeNotes: () => Promise.resolve(state.notes),
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
      notes: null,
    },
    isLoading: false,
    error: null,
    reload: vi.fn(),
  }),
}))

function note(overrides: Partial<EmployeeNote> = {}): EmployeeNote {
  return {
    id: 'note-1',
    employeeId: 'emp-1',
    text: 'Heeft hoogtevrees — nooit inplannen op kraanwerk.',
    isPinnedToDashboard: false,
    createdAt: '2026-07-28T10:00:00Z',
    createdByUserId: null,
    updatedAt: '2026-07-28T10:00:00Z',
    updatedByUserId: null,
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/employees/emp-1']}>
      <Routes>
        <Route path="/employees/:id" element={<EmployeeDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('EmployeeDetailPage — Notities panel (Overzicht, read-only users)', () => {
  beforeEach(() => {
    auth.permissions = new Set(['employee_notes.view'])
    state.notes = []
  })

  it('shows the notes panel on the profile tab for a read-only user with employee_notes.view', async () => {
    state.notes = [note()]
    renderPage()

    expect(screen.getByRole('heading', { name: 'Notities' })).toBeInTheDocument()
    expect(await screen.findByText('Heeft hoogtevrees — nooit inplannen op kraanwerk.')).toBeInTheDocument()
  })

  it('shows an empty-state message when the employee has no notes', async () => {
    renderPage()
    expect(screen.getByRole('heading', { name: 'Notities' })).toBeInTheDocument()
    expect(await screen.findByText('Nog geen notities voor deze medewerker.')).toBeInTheDocument()
  })

  it('hides the notes section entirely for a user without employee_notes.view', () => {
    auth.permissions = new Set()
    renderPage()
    expect(screen.queryByRole('heading', { name: 'Notities' })).not.toBeInTheDocument()
  })

  it('does not render the top-level notes card for a user who can edit the profile (it lives in the edit form instead)', () => {
    auth.permissions = new Set(['employees.edit', 'employee_notes.view'])
    state.notes = [note()]
    renderPage()
    expect(screen.queryByRole('heading', { name: 'Notities' })).not.toBeInTheDocument()
    expect(screen.getByTestId('employee-form')).toBeInTheDocument()
  })
})
