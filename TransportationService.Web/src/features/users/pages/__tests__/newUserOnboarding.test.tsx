import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NewUserPage } from '../NewUserPage'
import type { User } from '../../types/user'

const auth = vi.hoisted(() => ({ permissions: ['users.create', 'users.edit'] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: { id: 'admin' },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
const created: User = {
  id: 'u-9', email: 'test.hr@example.test', firstName: 'Test', lastName: 'Gebruiker',
  employeeId: null, customerId: null, isActive: true, isBlocked: false, roles: [], isActivated: false,
}
vi.mock('../../components/UserForm', () => ({
  UserForm: ({ onSaved }: { onSaved: (user: User) => void }) => (
    <button type="button" onClick={() => onSaved(created)}>Gebruiker aanmaken</button>
  ),
}))
const usersApi = vi.hoisted(() => ({
  startUserActivation: vi.fn(),
  activationUrl: (token: string) => `http://localhost/reset-password?token=${token}`,
}))
vi.mock('../../api/usersApi', () => usersApi)

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = ['users.create', 'users.edit']
  usersApi.startUserActivation.mockResolvedValue({ token: 'tok-123', expiresAtUtc: '2026-10-19T12:00:00Z' })
})

// HR acceptance H7: creating a user ends on its activation step, never on a dead "Actief" account.
describe('NewUserPage onboarding', () => {
  it('shows the one-time activation link right after creating the user', async () => {
    render(<MemoryRouter><NewUserPage /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Gebruiker aanmaken' }))

    expect(await screen.findByDisplayValue('http://localhost/reset-password?token=tok-123')).toBeInTheDocument()
    expect(usersApi.startUserActivation).toHaveBeenCalledWith('u-9')
    expect(screen.getByRole('button', { name: 'Naar de gebruiker' })).toBeInTheDocument()
  })

  it('explains the next step when the creator may not issue activation links', async () => {
    auth.permissions = ['users.create']
    render(<MemoryRouter><NewUserPage /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Gebruiker aanmaken' }))

    expect(await screen.findByText(/nog niet geactiveerd/)).toBeInTheDocument()
    expect(usersApi.startUserActivation).not.toHaveBeenCalled()
  })
})
