import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../../api/apiClient'
import { UserForm } from '../UserForm'

// MD-M8 (master-data remediation 2026-10-18): a duplicate e-mail (case-insensitive) is refused with
// a Dutch 400 on field `email`; the form shows it under the e-mail field instead of the generic
// "De actie kon niet worden uitgevoerd" — and a 409 race ({ message }) shows the same text.

const usersApi = vi.hoisted(() => ({ createUser: vi.fn(), updateUser: vi.fn() }))
vi.mock('../../api/usersApi', () => ({
  createUser: (...args: unknown[]) => usersApi.createUser(...args),
  updateUser: (...args: unknown[]) => usersApi.updateUser(...args),
  setUserActive: vi.fn(),
  setUserBlocked: vi.fn(),
  assignUserRoles: vi.fn(),
}))
vi.mock('../../../employees/api/employeesApi', () => ({ getEmployee: vi.fn(), searchEmployees: vi.fn() }))
vi.mock('../../../customers/api/customersApi', () => ({ getCustomer: vi.fn(), searchCustomers: vi.fn() }))

const DUPLICATE = 'Er bestaat al een gebruiker met dit e-mailadres (hoofdletters maken geen verschil).'

async function fillAndSubmit() {
  const user = userEvent.setup()
  render(
    <MemoryRouter>
      <UserForm onSaved={vi.fn()} />
    </MemoryRouter>,
  )
  await user.type(screen.getByLabelText('E-mail'), 'AN@dev.local')
  await user.type(screen.getByLabelText('Voornaam'), 'An')
  await user.type(screen.getByLabelText('Achternaam'), 'Peeters')
  await user.click(screen.getByRole('button', { name: 'Gebruiker aanmaken' }))
  return user
}

describe('UserForm — duplicate e-mail', () => {
  beforeEach(() => {
    usersApi.createUser.mockReset()
  })

  it('shows the server message under the e-mail field, once', async () => {
    usersApi.createUser.mockRejectedValue(
      new ApiError(DUPLICATE, 400, { detail: DUPLICATE, code: 'common.validation_failed', errors: { email: [DUPLICATE] } }),
    )
    const user = await fillAndSubmit()

    expect(await screen.findByText(DUPLICATE)).toHaveAttribute('id', 'email-error')
    expect(screen.getAllByText(DUPLICATE)).toHaveLength(1)
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true')

    // Editing the address clears the stale server message.
    await user.type(screen.getByLabelText('E-mail'), 'x')
    await waitFor(() => expect(screen.queryByText(DUPLICATE)).not.toBeInTheDocument())
  })

  it('shows the server message of a 409 race as the form error', async () => {
    usersApi.createUser.mockRejectedValue(new ApiError(DUPLICATE, 409, { message: DUPLICATE }))
    await fillAndSubmit()

    expect(await screen.findByRole('alert')).toHaveTextContent(DUPLICATE)
  })
})
