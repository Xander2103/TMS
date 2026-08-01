import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import * as api from '../api/tasksApi'
import { NewTaskDialog } from '../components/NewTaskDialog'
import { makeTask } from './taskTestData'

const auth = vi.hoisted(() => ({
  permissions: ['tasks.manage_own'],
  user: { id: 'user-1', employeeId: 'emp-1' },
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: auth.user,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../components/EmployeePicker', () => ({
  EmployeeSelect: () => null,
  EmployeeMultiSelect: ({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) => (
    <button type="button" aria-label="Medewerker toevoegen" onClick={() => onChange([...value, 'emp-2'])}>
      Medewerker toevoegen
    </button>
  ),
  useEmployeeOptions: () => ({ options: [], isLoading: false }),
}))

beforeEach(() => {
  vi.restoreAllMocks()
  toast.showSuccess.mockClear()
  auth.permissions = ['tasks.manage_own']
})

describe('NewTaskDialog', () => {
  it('hides the employee picker without tasks.assign and assigns to the user itself', async () => {
    const createSpy = vi.spyOn(api, 'createTasks').mockResolvedValue([makeTask()])

    render(<NewTaskDialog onClose={vi.fn()} onCreated={vi.fn()} />)
    expect(screen.queryByLabelText('Medewerker toevoegen')).not.toBeInTheDocument()
    expect(screen.getByText('Mijzelf')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText(/Titel/), { target: { value: 'Werkkledij ophalen' } })
    fireEvent.click(screen.getByRole('button', { name: 'Taak aanmaken' }))

    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Werkkledij ophalen', assignedEmployeeIds: ['emp-1'] }),
      ),
    )
  })

  it('shows the employee picker with tasks.assign', () => {
    auth.permissions = ['tasks.manage_own', 'tasks.assign']
    render(<NewTaskDialog onClose={vi.fn()} onCreated={vi.fn()} />)
    expect(screen.getByLabelText('Medewerker toevoegen')).toBeInTheDocument()
    expect(screen.queryByText('Mijzelf')).not.toBeInTheDocument()
  })
})
