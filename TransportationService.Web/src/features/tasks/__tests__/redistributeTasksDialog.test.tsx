import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import * as api from '../api/tasksApi'
import { RedistributeTasksDialog } from '../components/RedistributeTasksDialog'
import { makeSummary } from './taskTestData'

const auth = vi.hoisted(() => ({
  permissions: ['tasks.assign', 'tasks.cancel'],
  user: { id: 'user-1', employeeId: 'emp-9' },
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
// Decouple the dialog test from the combobox internals with a plain native select.
vi.mock('../components/EmployeePicker', () => ({
  EmployeeSelect: ({ value, onChange }: { value: string | null; onChange: (v: string | null) => void }) => (
    <select aria-label="Nieuwe medewerker" value={value ?? ''} onChange={(e) => onChange(e.target.value || null)}>
      <option value="">—</option>
      <option value="emp-2">Bob Willems</option>
    </select>
  ),
  EmployeeMultiSelect: () => null,
  useEmployeeOptions: () => ({ options: [], isLoading: false }),
}))

beforeEach(() => {
  vi.restoreAllMocks()
  toast.showSuccess.mockClear()
  vi.spyOn(api, 'getEmployeeOpenTaskSummary').mockResolvedValue(makeSummary())
})

describe('RedistributeTasksDialog', () => {
  it('sends the reassign payload and reports the affected count', async () => {
    const redistributeSpy = vi
      .spyOn(api, 'redistributeTasks')
      .mockResolvedValue({ affectedTasks: 4, action: 'reassign' })

    render(<RedistributeTasksDialog employeeId="emp-1" employeeName="An Peeters" onClose={vi.fn()} />)
    expect(await screen.findByText('Te doen: 2')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Nieuwe medewerker'), { target: { value: 'emp-2' } })
    fireEvent.change(screen.getByLabelText(/Reden/), { target: { value: 'Uit dienst' } })
    fireEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))

    await waitFor(() =>
      expect(redistributeSpy).toHaveBeenCalledWith({
        fromEmployeeId: 'emp-1',
        action: 'reassign',
        targetEmployeeId: 'emp-2',
        newDueAt: undefined,
        reason: 'Uit dienst',
      }),
    )
    await waitFor(() => expect(toast.showSuccess).toHaveBeenCalledWith('4 taken herverdeeld.'))
  })

  it('requires a reason before submitting', async () => {
    const redistributeSpy = vi.spyOn(api, 'redistributeTasks')

    render(<RedistributeTasksDialog employeeId="emp-1" employeeName="An Peeters" onClose={vi.fn()} />)
    fireEvent.change(await screen.findByLabelText('Nieuwe medewerker'), { target: { value: 'emp-2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Bevestigen' }))

    expect(await screen.findByText('Een reden is verplicht.')).toBeInTheDocument()
    expect(redistributeSpy).not.toHaveBeenCalled()
  })
})
