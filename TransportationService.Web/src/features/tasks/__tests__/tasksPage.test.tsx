import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom'
import * as api from '../api/tasksApi'
import { TasksPage } from '../pages/TasksPage'
import { makeTask } from './taskTestData'

const auth = vi.hoisted(() => ({
  permissions: ['tasks.view_own', 'tasks.view_all', 'tasks.assign', 'tasks.manage_own'],
  user: { id: 'user-1', employeeId: 'emp-1' },
}))

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
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [{ id: 'cat-1', code: 'ADM', name: 'Administratie' }],
    isLoading: false,
    error: null,
  }),
}))

vi.mock('../components/TaskDetailPanel', () => ({
  TaskDetailPanel: ({ taskId, onClose }: { taskId: string; onClose: () => void }) => (
    <div role="dialog" aria-label="Taakdetail">
      {taskId}
      <button type="button" onClick={onClose}>
        Detail sluiten
      </button>
    </div>
  ),
}))

function HistoryProbe() {
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <output data-testid="search">{location.search}</output>
      <button type="button" onClick={() => navigate(-1)}>
        Browser terug
      </button>
    </>
  )
}

function renderWithHistory(entries: string[]) {
  return render(
    <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
      <TasksPage />
      <HistoryProbe />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'listTasks').mockResolvedValue({
    items: [makeTask({ id: 'task-1', title: 'Rijbewijs controleren' })],
    total: 1,
    page: 1,
    pageSize: 25,
  })
})

function renderPage(initialEntry = '/tasks') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <TasksPage />
    </MemoryRouter>,
  )
}

describe('TasksPage', () => {
  it('renders the task list from the api', async () => {
    renderPage()
    expect(await screen.findByText('Rijbewijs controleren')).toBeInTheDocument()
    await waitFor(() =>
      expect(api.listTasks).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 25 })),
    )
  })

  it('passes the chosen filters to the api query', async () => {
    renderPage()
    await screen.findByText('Rijbewijs controleren')

    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Blocked' } })
    fireEvent.change(screen.getByLabelText('Prioriteit'), { target: { value: 'Urgent' } })
    fireEvent.click(screen.getByLabelText('Achterstallig'))

    await waitFor(() =>
      expect(api.listTasks).toHaveBeenCalledWith(
        expect.objectContaining({ status: 'Blocked', priority: 'Urgent', overdueOnly: true, page: 1 }),
      ),
    )
  })

  it('pre-activates the own-tasks filter for ?mine=1 deep links', async () => {
    renderPage('/tasks?mine=1')
    await screen.findByText('Rijbewijs controleren')
    expect(screen.getByLabelText('Mijn taken')).toBeChecked()
    await waitFor(() => expect(api.listTasks).toHaveBeenCalledWith(expect.objectContaining({ mine: true })))
  })

  it('pre-activates overdue, review and status filters from the query string', async () => {
    renderPage('/tasks?mine=1&overdue=1&review=1&status=Blocked')
    await screen.findByText('Rijbewijs controleren')

    expect(screen.getByLabelText('Achterstallig')).toBeChecked()
    expect(screen.getByLabelText('Wacht op controle')).toBeChecked()
    expect(screen.getByLabelText('Status')).toHaveValue('Blocked')
    await waitFor(() =>
      expect(api.listTasks).toHaveBeenCalledWith(
        expect.objectContaining({ mine: true, overdueOnly: true, waitingForReviewOnly: true, status: 'Blocked' }),
      ),
    )
  })

  it('ignores an unknown ?status= value', async () => {
    renderPage('/tasks?status=Onzin')
    await screen.findByText('Rijbewijs controleren')
    expect(screen.getByLabelText('Status')).toHaveValue('')
  })

  // HR acceptance #20: page 2 -> open a task -> Back used to land on page 1.
  it('Back after opening a task from page 2 closes the detail and stays on page 2 with its filters', async () => {
    renderWithHistory(['/tasks', '/tasks?status=Todo&page=2'])
    fireEvent.click(await screen.findByText('Rijbewijs controleren'))

    expect(await screen.findByRole('dialog', { name: 'Taakdetail' })).toBeInTheDocument()
    expect(screen.getByTestId('search').textContent).toBe('?status=Todo&taskId=task-1&page=2')

    fireEvent.click(screen.getByRole('button', { name: 'Browser terug' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Taakdetail' })).not.toBeInTheDocument())
    expect(screen.getByTestId('search').textContent).toBe('?status=Todo&page=2')
    await waitFor(() =>
      expect(vi.mocked(api.listTasks).mock.lastCall?.[0]).toEqual(expect.objectContaining({ page: 2, status: 'Todo' })),
    )
  })

  it('closing an opened task returns to the same list entry (no duplicate history entry)', async () => {
    renderWithHistory(['/tasks', '/tasks?page=2'])
    fireEvent.click(await screen.findByText('Rijbewijs controleren'))
    fireEvent.click(await screen.findByRole('button', { name: 'Detail sluiten' }))

    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?page=2'))
    // One more Back is the previous page, not a copy of page 2.
    fireEvent.click(screen.getByRole('button', { name: 'Browser terug' }))
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''))
  })

  it('closes a deep-linked task in place', async () => {
    renderWithHistory(['/tasks?taskId=task-1&page=2'])
    fireEvent.click(await screen.findByRole('button', { name: 'Detail sluiten' }))

    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?page=2'))
    expect(screen.queryByRole('dialog', { name: 'Taakdetail' })).not.toBeInTheDocument()
  })
})
