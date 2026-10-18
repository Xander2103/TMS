import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AuthProvider } from '../AuthContext'
import { useAuth } from '../authContextValue'
import { getAccessToken } from '../authStorage'

// Logout revokes the refresh-token family and the access token bound to it, so a second tab, a
// password change or an admin revocation can leave this tab holding a dead session. Signing out
// must still wipe the local credential when POST /api/auth/logout answers 401 (or never answers).

vi.mock('../../driver/offlineActions', () => ({ clearDriverOfflineState: vi.fn() }))

function Probe() {
  const { status, logout } = useAuth()
  return (
    <div>
      <p>status: {status}</p>
      <button type="button" onClick={() => void logout()}>Afmelden</button>
    </div>
  )
}

/** Session restore succeeds; the logout call gets `logoutResponse`. */
function stubBackend(logoutResponse: () => Promise<Response>) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/api/auth/refresh')) {
      return new Response(JSON.stringify({ accessToken: 'live-token', user: { id: 'u1', permissions: [] } }), { status: 200 })
    }
    if (url.endsWith('/api/auth/logout')) return logoutResponse()
    throw new Error(`unexpected call ${url}`)
  }))
}

async function signOut() {
  render(<AuthProvider><Probe /></AuthProvider>)
  expect(await screen.findByText('status: authenticated')).toBeInTheDocument()
  expect(getAccessToken()).toBe('live-token')
  await userEvent.click(screen.getByRole('button', { name: 'Afmelden' }))
  expect(await screen.findByText('status: unauthenticated')).toBeInTheDocument()
}

describe('logout with a session the server already killed', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('clears the local access token when the logout call answers 401', async () => {
    stubBackend(async () => new Response(null, { status: 401 }))
    await signOut()
    expect(getAccessToken()).toBeNull()
  })

  it('clears the local access token when the logout call cannot reach the server', async () => {
    stubBackend(async () => { throw new TypeError('Failed to fetch') })
    await signOut()
    expect(getAccessToken()).toBeNull()
  })
})
