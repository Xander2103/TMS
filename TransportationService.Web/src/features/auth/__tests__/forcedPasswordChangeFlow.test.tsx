import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, Outlet, Route, RouterProvider, createRoutesFromElements } from 'react-router-dom'
import { ToastProvider } from '../../../components/ui/ToastProvider'
import { RootRedirect } from '../../../routes/portalRouting'
import { AuthProvider } from '../AuthContext'
import { useAuth } from '../authContextValue'
import { LoginPage } from '../LoginPage'
import { ChangePasswordPage } from '../PasswordFlowPages'
import { RequireAuth } from '../RequireAuth'
import type { AuthTokens, CurrentUser } from '../authTypes'

/**
 * End-to-end routing flow with the REAL AuthProvider, RequireAuth, LoginPage, ChangePasswordPage
 * and RootRedirect. Only the HTTP layer is replaced, by a tiny stateful backend: an account with a
 * password and a MustChangePassword flag, and a session that a page reload restores via refresh().
 */
interface Account {
  email: string
  password: string
  mustChangePassword: boolean
  permissions: string[]
}

const backend = vi.hoisted(() => ({
  accounts: [] as Account[],
  session: null as string | null, // email of the signed-in account (the "refresh cookie")
}))

function tokensFor(account: Account): AuthTokens {
  const user: CurrentUser = {
    id: `id-${account.email}`, tenantId: 't1', tenantName: 'Acme', email: account.email,
    firstName: 'Test', lastName: 'User', employeeId: null, roles: [], permissions: account.permissions,
    mustChangePassword: account.mustChangePassword, customerId: null, preferredLanguage: 'nl',
  }
  return { accessToken: `token-${account.email}`, accessTokenExpiresAt: '', refreshToken: '', refreshTokenExpiresAt: '', user }
}

vi.mock('../authApi', () => {
  class LoginError extends Error {}
  return {
    LoginError,
    login: vi.fn(async (email: string, password: string) => {
      const account = backend.accounts.find((a) => a.email === email && a.password === password)
      if (!account) throw new LoginError('Ongeldige aanmeldgegevens')
      backend.session = account.email
      return tokensFor(account)
    }),
    refresh: vi.fn(async () => {
      const account = backend.accounts.find((a) => a.email === backend.session)
      return account ? tokensFor(account) : null
    }),
    logout: vi.fn(async () => {
      backend.session = null
    }),
  }
})

vi.mock('../../../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/apiClient')>()),
  apiClient: {
    postJson: vi.fn(async (url: string, body: { currentPassword: string; newPassword: string }) => {
      if (url !== '/api/me/password') throw new Error(`unexpected call ${url}`)
      const account = backend.accounts.find((a) => a.email === backend.session)
      if (!account || account.password !== body.currentPassword) throw new Error('Huidig wachtwoord is onjuist')
      // Mirrors UserAccountFlowService: new credential, flag cleared, every session revoked.
      account.password = body.newPassword
      account.mustChangePassword = false
    }),
  },
}))

vi.mock('../../driver/offlineActions', () => ({ clearDriverOfflineState: vi.fn() }))

function Page({ name }: { name: string }) {
  const { logout } = useAuth()
  return (
    <div>
      <h1>{name}</h1>
      <button type="button" onClick={() => void logout()}>Afmelden</button>
    </div>
  )
}

function Providers() {
  return (
    <AuthProvider>
      <ToastProvider>
        <Outlet />
      </ToastProvider>
    </AuthProvider>
  )
}

/** Mounts the app at `path`; calling it again after cleanup() is a full page reload (F5). */
function mountApp(path: string) {
  const router = createMemoryRouter(
    createRoutesFromElements(
      <Route element={<Providers />}>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/change-password" element={<ChangePasswordPage />} />
          <Route path="/dashboard" element={<Page name="Dashboard" />} />
          <Route path="/dossiers" element={<Page name="Dossiers" />} />
          <Route path="/transport-orders" element={<Page name="Opdrachten" />} />
          <Route path="/employees" element={<Page name="Personeel" />} />
        </Route>
      </Route>,
    ),
    { initialEntries: [path] },
  )
  render(<RouterProvider router={router} />)
  return router
}

async function signIn(email: string, password: string) {
  await userEvent.type(await screen.findByLabelText(/E-mailadres/), email)
  await userEvent.type(screen.getByLabelText(/^Wachtwoord/), password)
  await userEvent.click(screen.getByRole('button', { name: 'Inloggen' }))
}

async function chooseOwnPassword(current: string, next: string) {
  await userEvent.type(await screen.findByLabelText(/Tijdelijk \(huidig\) wachtwoord/), current)
  await userEvent.type(screen.getByLabelText(/^Nieuw wachtwoord/), next)
  await userEvent.type(screen.getByLabelText(/Bevestig nieuw wachtwoord/), next)
  await userEvent.click(screen.getByRole('button', { name: 'Wachtwoord wijzigen' }))
}

const HR = ['dashboard.view', 'employees.view', 'issued_items.manage_templates'] // no dossiers.view, no orders.view
const PLANNER = ['dashboard.view', 'dossiers.view', 'orders.view']

beforeEach(() => {
  backend.session = null
  backend.accounts = [
    { email: 'new.hr@acme.test', password: 'Temp-12345', mustChangePassword: true, permissions: HR },
    { email: 'new.planner@acme.test', password: 'Temp-12345', mustChangePassword: true, permissions: PLANNER },
    { email: 'settled.planner@acme.test', password: 'Own-12345', mustChangePassword: false, permissions: PLANNER },
  ]
})

describe('forced password change — full flow', () => {
  it('HR (no dossiers.view / orders.view): change → sign in → permitted landing page, never /change-password again', async () => {
    const router = mountApp('/')
    await signIn('new.hr@acme.test', 'Temp-12345')

    // Temporary credential: nothing but the change screen is reachable.
    expect(await screen.findByRole('heading', { name: 'Kies je eigen wachtwoord' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/change-password')

    await chooseOwnPassword('Temp-12345', 'Own-98765')

    // Backend confirmed and cleared the flag; every session is revoked, so the user signs in again.
    expect(backend.accounts[0]).toMatchObject({ password: 'Own-98765', mustChangePassword: false })
    await signIn('new.hr@acme.test', 'Own-98765')

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dashboard')
    expect(screen.queryByRole('heading', { name: 'Kies je eigen wachtwoord' })).not.toBeInTheDocument()
  })

  it('stays correct after F5 and after signing out and in again', async () => {
    let router = mountApp('/')
    await signIn('new.hr@acme.test', 'Temp-12345')
    await chooseOwnPassword('Temp-12345', 'Own-98765')
    await signIn('new.hr@acme.test', 'Own-98765')
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()

    // F5: a fresh app instance restores the session from the refresh cookie.
    const pathBeforeReload = router.state.location.pathname
    cleanup()
    router = mountApp(pathBeforeReload)
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dashboard')

    // Sign out, sign in again: still the landing page, still no change screen.
    await userEvent.click(screen.getByRole('button', { name: 'Afmelden' }))
    await signIn('new.hr@acme.test', 'Own-98765')
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dashboard')
  })

  it('a role with dossiers.view lands on /dossiers after the change (permission-driven, not hardcoded)', async () => {
    const router = mountApp('/')
    await signIn('new.planner@acme.test', 'Temp-12345')
    await chooseOwnPassword('Temp-12345', 'Own-98765')
    await signIn('new.planner@acme.test', 'Own-98765')

    expect(await screen.findByRole('heading', { name: 'Dossiers' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dossiers')
  })

  it('the old temporary password no longer signs in', async () => {
    mountApp('/')
    await signIn('new.hr@acme.test', 'Temp-12345')
    await chooseOwnPassword('Temp-12345', 'Own-98765')
    await signIn('new.hr@acme.test', 'Temp-12345')

    expect(await screen.findByRole('alert')).toHaveTextContent('Ongeldige aanmeldgegevens')
    expect(screen.queryByRole('heading', { name: 'Dashboard' })).not.toBeInTheDocument()
  })
})

describe('return-to-page after sign-in', () => {
  it('an explicit sign-out does NOT carry the page over to the next sign-in (possibly another user)', async () => {
    const router = mountApp('/employees')
    await signIn('settled.planner@acme.test', 'Own-12345')
    expect(await screen.findByRole('heading', { name: 'Personeel' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Afmelden' }))
    await signIn('settled.planner@acme.test', 'Own-12345')

    expect(await screen.findByRole('heading', { name: 'Dossiers' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dossiers')
  })

  it('an unauthenticated deep link still returns to the requested page after sign-in', async () => {
    const router = mountApp('/employees')
    await signIn('settled.planner@acme.test', 'Own-12345')

    expect(await screen.findByRole('heading', { name: 'Personeel' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/employees')
  })

  it('a deep link keeps its target across the forced change is NOT required — the change screen wins first', async () => {
    const router = mountApp('/employees')
    await signIn('new.hr@acme.test', 'Temp-12345')

    expect(await screen.findByRole('heading', { name: 'Kies je eigen wachtwoord' })).toBeInTheDocument()
    await waitFor(() => expect(router.state.location.pathname).toBe('/change-password'))
  })
})

describe('/change-password without a temporary credential', () => {
  it('sends a user whose flag is already cleared to the permission-driven landing page', async () => {
    backend.session = 'settled.planner@acme.test'
    const router = mountApp('/change-password')

    expect(await screen.findByRole('heading', { name: 'Dossiers' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dossiers')
  })
})
