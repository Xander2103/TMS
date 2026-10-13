import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, createRoutesFromElements, Outlet, Route, RouterProvider } from 'react-router-dom'
import { AuthProvider } from '../AuthContext'
import { RequireAuth } from '../RequireAuth'
import type { AuthTokens, CurrentUser, RefreshOutcomeLike } from './sessionBootstrap.helpers'

/**
 * B01 session bootstrap: a server that cannot answer the refresh (429 / 5xx / network) must not
 * drop the user on the login page. The restore retries with backoff, then shows a recoverable
 * "unavailable" screen; a rejected credential still goes to /login.
 */
const refreshSpy = vi.hoisted(() => vi.fn())

vi.mock('../authApi', () => ({
  LoginError: class extends Error {},
  login: vi.fn(),
  refresh: refreshSpy,
  logout: vi.fn(),
}))
vi.mock('../../driver/offlineActions', () => ({ clearDriverOfflineState: vi.fn() }))

function okTokens(): AuthTokens {
  const user: CurrentUser = {
    id: 'u1', tenantId: 't1', tenantName: 'Acme', email: 'a@b.c', firstName: 'Test', lastName: 'User',
    employeeId: null, roles: [], permissions: ['dossiers.view'], mustChangePassword: false, customerId: null,
    preferredLanguage: 'nl', themePreference: null,
  }
  return { accessToken: 'tok', accessTokenExpiresAt: '', refreshToken: '', refreshTokenExpiresAt: '', user }
}

const OK: RefreshOutcomeLike = { status: 'ok', tokens: okTokens() }
const RATE_LIMITED: RefreshOutcomeLike = { status: 'unavailable', reason: 'rate_limited', httpStatus: 429 }
const UNAUTHENTICATED: RefreshOutcomeLike = { status: 'unauthenticated' }

function renderApp() {
  const router = createMemoryRouter(
    createRoutesFromElements(
      <Route element={<AuthProvider retryDelaysMs={[0, 0]}><Outlet /></AuthProvider>}>
        <Route path="/login" element={<div>Aanmelden</div>} />
        <Route element={<RequireAuth />}>
          <Route path="/dossiers" element={<div>Dossierlijst</div>} />
        </Route>
      </Route>,
    ),
    { initialEntries: ['/dossiers'] },
  )
  render(<RouterProvider router={router} />)
  return router
}

describe('session bootstrap (B01)', () => {
  beforeEach(() => refreshSpy.mockReset())
  afterEach(() => cleanup())

  it('retries a rate-limited refresh and lands on the requested page once it succeeds', async () => {
    refreshSpy.mockResolvedValueOnce(RATE_LIMITED).mockResolvedValueOnce(RATE_LIMITED).mockResolvedValueOnce(OK)
    renderApp()
    expect(await screen.findByText('Dossierlijst')).toBeInTheDocument()
    expect(refreshSpy).toHaveBeenCalledTimes(3)
    expect(screen.queryByText('Aanmelden')).not.toBeInTheDocument()
  })

  it('shows a recoverable "unavailable" screen instead of the login page, and recovers on retry', async () => {
    const user = userEvent.setup()
    refreshSpy.mockResolvedValue(RATE_LIMITED)
    const router = renderApp()

    expect(await screen.findByRole('alert')).toHaveTextContent('De server is tijdelijk niet bereikbaar')
    expect(screen.getByText(/te druk is/)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dossiers')
    expect(screen.queryByText('Aanmelden')).not.toBeInTheDocument()
    expect(refreshSpy).toHaveBeenCalledTimes(3) // first attempt + two retries

    refreshSpy.mockResolvedValue(OK)
    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))
    expect(await screen.findByText('Dossierlijst')).toBeInTheDocument()
    expect(refreshSpy).toHaveBeenCalledTimes(4) // manual retry is a single attempt
  })

  it('still sends a rejected credential to the login page', async () => {
    refreshSpy.mockResolvedValue(UNAUTHENTICATED)
    renderApp()
    expect(await screen.findByText('Aanmelden')).toBeInTheDocument()
    await waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1))
  })

  it('does not retry a network failure forever: three attempts, then the unavailable screen', async () => {
    refreshSpy.mockResolvedValue({ status: 'unavailable', reason: 'network' })
    renderApp()
    expect(await screen.findByRole('alert')).toHaveTextContent('geen verbinding met de server')
    expect(refreshSpy).toHaveBeenCalledTimes(3)
  })
})
