import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerPortalUsersPage } from '../CustomerPortalUsersPage'
import type { PortalUserListItem } from '../../api/customerPortalApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const users = vi.hoisted(() => ({ value: [] as PortalUserListItem[] }))
const inviteSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerPortalApi', () => ({
  listPortalUsers: () => Promise.resolve(users.value),
  invitePortalUser: inviteSpy,
  deactivatePortalUser: vi.fn(),
  reactivatePortalUser: vi.fn(),
  resendPortalUserInvite: vi.fn(),
  setPortalUserGrants: vi.fn(),
}))

describe('CustomerPortalUsersPage', () => {
  beforeEach(() => {
    users.value = []
    inviteSpy.mockReset()
  })

  it('validates the invite form before calling the API', async () => {
    const user = userEvent.setup()
    render(<CustomerPortalUsersPage />)

    await waitFor(() => expect(screen.getByText('Nog geen gebruikers uitgenodigd.')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Gebruiker uitnodigen' }))
    await user.click(screen.getByRole('button', { name: 'Uitnodigen' }))

    expect(await screen.findByText('Vul voornaam, achternaam en e-mailadres in.')).toBeInTheDocument()
    expect(inviteSpy).not.toHaveBeenCalled()
  })

  it('submits the invite with the selected grants once the form is valid', async () => {
    inviteSpy.mockResolvedValue({
      user: {
        id: 'u2', email: 'nieuw@haven.be', firstName: 'Nieuwe', lastName: 'Klant',
        isActive: true, isBlocked: false, hasPendingActivation: true,
        grants: { documents: true, invoices: false, manageUsers: false },
      },
      activationToken: 'raw-token',
      activationTokenExpiresAtUtc: '2026-08-02T00:00:00Z',
    })
    const user = userEvent.setup()
    render(<CustomerPortalUsersPage />)

    await waitFor(() => expect(screen.getByText('Nog geen gebruikers uitgenodigd.')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Gebruiker uitnodigen' }))
    await user.type(screen.getByLabelText(/^Voornaam/), 'Nieuwe')
    await user.type(screen.getByLabelText(/^Achternaam/), 'Klant')
    await user.type(screen.getByLabelText(/^E-mailadres/), 'nieuw@haven.be')
    await user.click(screen.getByLabelText('Documenten bekijken'))
    await user.click(screen.getByRole('button', { name: 'Uitnodigen' }))

    await waitFor(() =>
      expect(inviteSpy).toHaveBeenCalledWith({
        firstName: 'Nieuwe',
        lastName: 'Klant',
        email: 'nieuw@haven.be',
        grants: { documents: true, invoices: false, manageUsers: false },
      }),
    )
  })
})
