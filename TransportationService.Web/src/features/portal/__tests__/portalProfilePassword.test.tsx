import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { PortalProfilePage } from '../pages/PortalProfilePage'
import type { MyProfile } from '../types'

// SEC-M2: POST /api/me/password revokes EVERY session of the user, the current one included, so
// the self-service change ends in a sign-out and a fresh sign-in with the new password.

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), ...toast }),
}))

const logout = vi.hoisted(() => vi.fn(async () => {}))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ logout, hasPermission: () => false, hasAnyPermission: () => false }),
}))

const api = vi.hoisted(() => ({ changeMyPassword: vi.fn() }))
vi.mock('../api/portalApi', () => ({
  getMyProfile: vi.fn(async (): Promise<MyProfile> => ({
    employeeId: 'e1', employeeNumber: 'EMP-001', firstName: 'Jan', lastName: 'Peeters', email: 'jan@acme.test',
    phoneNumber: '0470 00 00 00', mobilePhone: null, street: 'Kerkstraat', houseNumber: '1', postalCode: '9000',
    city: 'Gent', countryCode: 'BE', emergencyContactName: null, emergencyContactPhone: null,
    employmentStartDate: '2024-01-01', employmentStatus: 'Active', departmentName: null, jobFunctions: [],
    isDriver: false, driverNumber: null,
  })),
  changeMyPassword: api.changeMyPassword,
}))

// Neither card is under test; both would load their own data.
vi.mock('../../leave-balance/components/MyLeaveBalanceCard', () => ({ MyLeaveBalanceCard: () => null }))
vi.mock('../../../theme/AppearanceSettings', () => ({ AppearanceSettings: () => null }))

function mount() {
  const router = createMemoryRouter(
    [
      { path: '/portal/profile', element: <PortalProfilePage /> },
      { path: '/login', element: <h1>Aanmelden</h1> },
    ],
    { initialEntries: ['/', '/portal/profile'], initialIndex: 1 },
  )
  render(<RouterProvider router={router} />)
  return router
}

async function changePassword() {
  await userEvent.type(await screen.findByLabelText(/^Huidig wachtwoord/), 'Old-12345')
  await userEvent.type(screen.getByLabelText(/^Nieuw wachtwoord/), 'New-98765')
  await userEvent.type(screen.getByLabelText(/^Bevestig nieuw wachtwoord/), 'New-98765')
  await userEvent.click(screen.getByRole('button', { name: 'Wachtwoord wijzigen' }))
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('portal profile — password change', () => {
  it('tells the user up front that every session is signed out', async () => {
    mount()
    expect(await screen.findByText(/op al je toestellen afgemeld/)).toBeInTheDocument()
  })

  it('on success: toast, sign-out, then /login replacing the profile in the history', async () => {
    api.changeMyPassword.mockResolvedValue(undefined)
    const router = mount()
    await changePassword()

    expect(await screen.findByRole('heading', { name: 'Aanmelden' })).toBeInTheDocument()
    expect(api.changeMyPassword).toHaveBeenCalledWith('Old-12345', 'New-98765')
    expect(toast.showSuccess).toHaveBeenCalledWith('Je wachtwoord is gewijzigd.')
    expect(logout).toHaveBeenCalledTimes(1)
    expect(router.state.location.pathname).toBe('/login')
    // replace: going back does not return to the profile of a session that no longer exists.
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('on failure: stays on the page, keeps the session and re-enables the form', async () => {
    api.changeMyPassword.mockRejectedValue(new ApiError('Huidig wachtwoord is onjuist', 400))
    const router = mount()
    await changePassword()

    await waitFor(() => expect(toast.showError).toHaveBeenCalledWith('Huidig wachtwoord is onjuist'))
    expect(logout).not.toHaveBeenCalled()
    expect(router.state.location.pathname).toBe('/portal/profile')
    expect(screen.getByRole('button', { name: 'Wachtwoord wijzigen' })).toBeEnabled()
  })
})
