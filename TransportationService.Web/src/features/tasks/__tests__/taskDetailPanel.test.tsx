import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ApiError } from '../../../api/apiClient'
import * as api from '../api/tasksApi'
import { TaskDetailPanel } from '../components/TaskDetailPanel'
import { makeTask } from './taskTestData'

const auth = vi.hoisted(() => ({
  permissions: ['tasks.view_own', 'tasks.manage_own'],
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

beforeEach(() => {
  vi.restoreAllMocks()
  toast.showSuccess.mockClear()
  toast.showError.mockClear()
  auth.permissions = ['tasks.view_own', 'tasks.manage_own']
  auth.user = { id: 'user-1', employeeId: 'emp-1' }
  vi.spyOn(api, 'listTaskAttachments').mockResolvedValue([])
})

describe('TaskDetailPanel', () => {
  it('sends expectedVersion with a status action and shows the backend message on 400', async () => {
    const task = makeTask({ status: 'Todo', version: 7 })
    const getTaskSpy = vi.spyOn(api, 'getTask').mockResolvedValue(task)
    vi.spyOn(api, 'startTask').mockRejectedValue(
      new ApiError('De taak is intussen gewijzigd. Herlaad en probeer opnieuw.', 400),
    )

    render(<TaskDetailPanel taskId="task-1" onClose={vi.fn()} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Start' }))

    await waitFor(() => expect(api.startTask).toHaveBeenCalledWith('task-1', { expectedVersion: 7 }))
    await waitFor(() =>
      expect(toast.showError).toHaveBeenCalledWith('De taak is intussen gewijzigd. Herlaad en probeer opnieuw.'),
    )
    // The conflicting task is reloaded (initial load + refresh after the 400).
    await waitFor(() => expect(getTaskSpy).toHaveBeenCalledTimes(2))
  })

  it('requires a reason before a task can be blocked', async () => {
    vi.spyOn(api, 'getTask').mockResolvedValue(makeTask({ status: 'InProgress', version: 2 }))
    const blockSpy = vi.spyOn(api, 'blockTask').mockResolvedValue(makeTask({ status: 'Blocked' }))

    render(<TaskDetailPanel taskId="task-1" onClose={vi.fn()} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Blokkeer' }))
    // Confirm inside the dialog without entering a reason.
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Taak blokkeren' })).getByRole('button', { name: 'Blokkeer' }))

    expect(await screen.findByText('Dit veld is verplicht.')).toBeInTheDocument()
    expect(blockSpy).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText(/Reden/), { target: { value: 'Wachten op materiaal' } })
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Taak blokkeren' })).getByRole('button', { name: 'Blokkeer' }))
    await waitFor(() =>
      expect(blockSpy).toHaveBeenCalledWith('task-1', { expectedVersion: 2, note: 'Wachten op materiaal' }),
    )
  })

  it('hides the review buttons for the assignee', async () => {
    auth.permissions = ['tasks.view_own', 'tasks.manage_own', 'tasks.review']
    vi.spyOn(api, 'getTask').mockResolvedValue(
      makeTask({ status: 'WaitingForReview', assignedEmployeeId: 'emp-1', requiresReview: true }),
    )

    render(<TaskDetailPanel taskId="task-1" onClose={vi.fn()} />)
    expect(await screen.findByText('Wacht op controle')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Goedkeuren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Afkeuren' })).not.toBeInTheDocument()
  })

  it('shows the review buttons for a reviewer who is not the assignee', async () => {
    auth.user = { id: 'user-2', employeeId: 'emp-2' }
    auth.permissions = ['tasks.view_all', 'tasks.review']
    vi.spyOn(api, 'getTask').mockResolvedValue(
      makeTask({ status: 'WaitingForReview', assignedEmployeeId: 'emp-1', requiresReview: true }),
    )

    render(<TaskDetailPanel taskId="task-1" onClose={vi.fn()} />)
    expect(await screen.findByRole('button', { name: 'Goedkeuren' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Afkeuren' })).toBeInTheDocument()
  })
})
