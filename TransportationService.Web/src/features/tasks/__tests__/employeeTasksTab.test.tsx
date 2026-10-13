import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import * as api from '../api/tasksApi'
import { EmployeeTasksTab } from '../components/EmployeeTasksTab'
import { makeSummary, makeTask } from './taskTestData'

const auth = vi.hoisted(() => ({
  permissions: ['tasks.view_all'],
  user: { id: 'user-1', employeeId: 'emp-9' },
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

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'getEmployeeOpenTaskSummary').mockResolvedValue(
    makeSummary({ todo: 2, inProgress: 1, blocked: 0, waitingForReview: 1, overdue: 1 }),
  )
  vi.spyOn(api, 'listEmployeeTasks').mockResolvedValue([makeTask({ id: 'task-1', title: 'Onboardingdossier' })])
})

describe('EmployeeTasksTab', () => {
  it('renders the open-summary badges and the task list', async () => {
    render(<EmployeeTasksTab employeeId="emp-1" />)

    expect(await screen.findByText('Te doen: 2')).toBeInTheDocument()
    expect(screen.getByText('In uitvoering: 1')).toBeInTheDocument()
    expect(screen.getByText('Geblokkeerd: 0')).toBeInTheDocument()
    expect(screen.getByText('Wacht op controle: 1')).toBeInTheDocument()
    expect(screen.getByText('Achterstallig: 1')).toBeInTheDocument()
    expect(await screen.findByText('Onboardingdossier')).toBeInTheDocument()
    expect(api.listEmployeeTasks).toHaveBeenCalledWith('emp-1')
    expect(api.getEmployeeOpenTaskSummary).toHaveBeenCalledWith('emp-1')
  })
})
