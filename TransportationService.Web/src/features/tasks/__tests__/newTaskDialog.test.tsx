import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ApiError } from '../../../api/apiClient'
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

  // HR acceptance H1: datetime-local values are tenant wall clock and go out as UTC instants
  // (22/10 is summer time, UTC+2) — a zone-less value used to make the API return a 500.
  it('sends start and deadline as UTC instants', async () => {
    const createSpy = vi.spyOn(api, 'createTasks').mockResolvedValue([makeTask()])
    render(<NewTaskDialog onClose={vi.fn()} onCreated={vi.fn()} />)

    fireEvent.change(screen.getByLabelText(/Titel/), { target: { value: 'Medische keuring' } })
    fireEvent.change(screen.getByLabelText('Start'), { target: { value: '2026-10-22T08:00' } })
    fireEvent.change(screen.getByLabelText('Deadline'), { target: { value: '2026-10-22T17:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Taak aanmaken' }))

    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith(
        expect.objectContaining({ startAt: '2026-10-22T06:00:00Z', dueAt: '2026-10-22T15:00:00Z' }),
      ),
    )
  })

  it('refuses a deadline before the start without calling the API', async () => {
    const createSpy = vi.spyOn(api, 'createTasks').mockResolvedValue([makeTask()])
    render(<NewTaskDialog onClose={vi.fn()} onCreated={vi.fn()} />)

    fireEvent.change(screen.getByLabelText(/Titel/), { target: { value: 'x' } })
    fireEvent.change(screen.getByLabelText('Start'), { target: { value: '2026-10-22T17:00' } })
    fireEvent.change(screen.getByLabelText('Deadline'), { target: { value: '2026-10-22T08:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Taak aanmaken' }))

    expect(await screen.findByText('De deadline mag niet vóór het startmoment liggen.')).toBeInTheDocument()
    expect(createSpy).not.toHaveBeenCalled()
  })

  // HR acceptance #5: a scope refusal used to surface as the generic "De invoer is ongeldig".
  it('shows the specific assign-scope refusal, in the toast and at the employee picker', async () => {
    auth.permissions = ['tasks.manage_own', 'tasks.assign']
    const message = 'Je kan alleen taken toewijzen aan medewerkers van je eigen afdeling (HR); Otto Overkant hoort daar niet bij.'
    vi.spyOn(api, 'createTasks').mockRejectedValue(
      new ApiError(message, 400, {
        title: 'Validatiefout',
        detail: message,
        status: 400,
        code: 'common.validation_failed',
        errors: { assignedEmployeeIds: [message] },
      }),
    )
    render(<NewTaskDialog onClose={vi.fn()} onCreated={vi.fn()} />)

    fireEvent.change(screen.getByLabelText(/Titel/), { target: { value: 'Opleiding plannen' } })
    fireEvent.click(screen.getByLabelText('Medewerker toevoegen'))
    fireEvent.click(screen.getByRole('button', { name: 'Taak aanmaken' }))

    await waitFor(() => expect(toast.showError).toHaveBeenCalledWith(message))
    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(toast.showError).not.toHaveBeenCalledWith(expect.stringContaining('De invoer is ongeldig'))
  })
})
