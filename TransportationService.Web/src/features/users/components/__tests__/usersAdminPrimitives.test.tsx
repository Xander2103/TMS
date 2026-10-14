import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { UsersTable } from '../UsersTable'
import { UserForm } from '../UserForm'
import type { User } from '../../types/user'

vi.mock('../../hooks/useUserMutations', () => ({
  useUserMutations: () => ({ isSubmitting: false, error: null, create: vi.fn(), update: vi.fn() }),
}))
const employeesApi = vi.hoisted(() => ({ getEmployee: vi.fn(), searchEmployees: vi.fn() }))
vi.mock('../../../employees/api/employeesApi', () => employeesApi)
const customersApi = vi.hoisted(() => ({ getCustomer: vi.fn(), searchCustomers: vi.fn() }))
vi.mock('../../../customers/api/customersApi', () => customersApi)

function user(overrides: Partial<User> = {}): User {
  return {
    id: 'u-1',
    email: 'an@dev.local',
    firstName: 'An',
    lastName: 'Peeters',
    employeeId: null,
    customerId: null,
    isActive: true,
    isBlocked: false,
    roles: [{ id: 'r-1', name: 'Planner' }],
    ...overrides,
  }
}

describe('Users admin on shared primitives (B46)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    employeesApi.getEmployee.mockResolvedValue({ id: 'emp-1', firstName: 'An', lastName: 'Peeters', employeeNumber: 'P-007' })
    customersApi.getCustomer.mockResolvedValue({ id: 'cus-1', name: 'Nexans NV', customerNumber: 'KL-0001' })
    employeesApi.searchEmployees.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 20 })
    customersApi.searchCustomers.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 20 })
  })

  it('renders the users list as a DataTable with status badges', () => {
    render(
      <MemoryRouter>
        <UsersTable users={[user(), user({ id: 'u-2', firstName: 'Bart', isActive: false, isBlocked: true, roles: [] })]} />
      </MemoryRouter>,
    )
    const table = screen.getByRole('table')
    expect(table).toHaveClass('data-table')
    expect(screen.getByText('Geblokkeerd')).toHaveClass('ui-badge')
    expect(screen.getByText('Inactief')).toHaveClass('ui-badge')
    expect(screen.getByText('Planner')).toHaveClass('ui-badge')
    expect(screen.getByText('Geen rollen')).toBeInTheDocument()
  })

  it('links employee and customer through a searchable select that shows the linked names', async () => {
    render(<UserForm user={user({ employeeId: 'emp-1', customerId: 'cus-1' })} onSaved={vi.fn()} />)
    expect(screen.getByRole('combobox', { name: 'Gekoppelde medewerker' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Gekoppelde klant' })).toBeInTheDocument()
    expect(await screen.findByDisplayValue('An Peeters (P-007)')).toBeInTheDocument()
    expect(await screen.findByDisplayValue('Nexans NV (KL-0001)')).toBeInTheDocument()
    // No raw id field is left on the form.
    expect(screen.queryByDisplayValue('emp-1')).not.toBeInTheDocument()
  })
})
