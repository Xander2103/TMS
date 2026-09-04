import { act, render, screen, waitFor } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { DisplayPreferencesProvider } from '../DisplayPreferencesProvider'
import { resetDisplayPreferences } from '../displayPreferences'
import { AuthContext, type AuthContextValue } from '../../../features/auth/authContextValue'
import type { CurrentUser } from '../../../features/auth/authTypes'
import {
  formatDateTime, resetDateFormatPreference, resetTimeZonePreference,
} from '../../../utils/dates'

/**
 * C-03 / I-2: the tenant zone must be known BEFORE a routed page renders a timestamp, and a
 * failing or slow fetch must never block the app. The old shape wrote module-level state from a
 * plain effect with no gate and no subscriber, so a page whose own data resolved first rendered
 * its windows in the default zone and never corrected itself.
 *
 * The process zone is forced to America/New_York so neither the browser zone (02:00) nor UTC
 * (06:00) can be mistaken for a correct tenant reading.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const api = vi.hoisted(() => ({ getJson: vi.fn() }))
vi.mock('../../../api/apiClient', () => ({ apiClient: { getJson: api.getJson } }))

/** Stands in for any routed page that renders an operational timestamp. */
function Probe() {
  return <span data-testid="stamp">{formatDateTime('2026-07-15T06:00:00Z')}</span>
}

function renderGate() {
  return render(
    <DisplayPreferencesProvider fallback={<span>voorkeuren laden</span>}>
      <Probe />
    </DisplayPreferencesProvider>,
  )
}

/** A signed-in session; only the identity fields matter to the bootstrap. */
function user(id: string, tenantId: string): CurrentUser {
  return { id, tenantId, permissions: [], roles: [] } as unknown as CurrentUser
}

function authValue(current: CurrentUser): AuthContextValue {
  return {
    status: 'authenticated',
    user: current,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => true,
    hasAnyPermission: () => true,
  }
}

function gateFor(current: CurrentUser) {
  return (
    <AuthContext.Provider value={authValue(current)}>
      <DisplayPreferencesProvider fallback={<span>voorkeuren laden</span>}>
        <Probe />
      </DisplayPreferencesProvider>
    </AuthContext.Provider>
  )
}

function renderGateAs(current: CurrentUser) {
  return render(gateFor(current))
}

beforeEach(() => {
  api.getJson.mockReset()
  resetDisplayPreferences()
  resetTimeZonePreference()
  resetDateFormatPreference()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('DisplayPreferencesProvider', () => {
  it('holds the routed content back until the preferences resolve, then renders the tenant zone', async () => {
    let resolvePrefs: (value: unknown) => void = () => {}
    api.getJson.mockReturnValue(new Promise((resolve) => { resolvePrefs = resolve }))

    renderGate()

    // Nothing that could carry a wrong-zone timestamp has rendered yet.
    expect(screen.getByText('voorkeuren laden')).toBeInTheDocument()
    expect(screen.queryByTestId('stamp')).not.toBeInTheDocument()

    await act(async () => {
      resolvePrefs({ dateFormat: 'yyyy-MM-dd', decimalSeparator: ',', timezone: 'Asia/Tokyo' })
    })

    // Tokyo, from the very first render of the page: 06:00Z is 15:00 there.
    expect(await screen.findByTestId('stamp')).toHaveTextContent('2026-07-15 15:00')
  })

  it('falls back to the defaults and still renders when the fetch fails', async () => {
    api.getJson.mockRejectedValue(new Error('offline'))

    renderGate()

    // Europe/Amsterdam + dd/MM/yyyy — the seeded backend defaults, never the browser zone.
    expect(await screen.findByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')
  })

  it('never blocks the app on a hanging request; a late answer still applies to what renders next', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let resolvePrefs: (value: unknown) => void = () => {}
    api.getJson.mockReturnValue(new Promise((resolve) => { resolvePrefs = resolve }))

    renderGate()
    expect(screen.getByText('voorkeuren laden')).toBeInTheDocument()

    await act(async () => {
      vi.advanceTimersByTime(5000)
    })

    // The gate opened on the defaults rather than holding the app hostage.
    expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')

    await act(async () => {
      resolvePrefs({ dateFormat: 'dd/MM/yyyy', timezone: 'Asia/Tokyo' })
    })

    // The late answer is applied to the formatters, so the NEXT page (a navigation, a refetch)
    // is correct. Already-mounted content is deliberately not remounted — silently throwing away
    // a half-typed form to fix a clock would be the worse trade.
    await waitFor(() => expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 08:00'))
    render(<Probe />)
    const stamps = await screen.findAllByTestId('stamp')
    expect(stamps[stamps.length - 1]).toHaveTextContent('15/07/2026 15:00')
  })

  it('refetches for the NEXT session: a logout/login on the same tab never reuses the first tenant zone', async () => {
    // The memo that dedupes the shells (R6) must not outlive the session it belongs to.
    // logout() and login() are pure SPA transitions — no page reload — so the module-level cache
    // is the only thing standing between tenant A's zone and tenant B's screens.
    api.getJson
      .mockResolvedValueOnce({ dateFormat: 'dd/MM/yyyy', decimalSeparator: ',', timezone: 'Europe/Amsterdam' })
      .mockResolvedValueOnce({ dateFormat: 'yyyy-MM-dd', decimalSeparator: '.', timezone: 'Asia/Tokyo' })

    const sessionA = renderGateAs(user('u-a', 'tenant-a'))
    expect(await screen.findByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')

    // Logout: RequireAuth renders <Navigate to="/login">, unmounting the shell and the gate.
    sessionA.unmount()

    // Login as somebody else — a different tenant, on a different zone.
    renderGateAs(user('u-b', 'tenant-b'))

    expect(await screen.findByTestId('stamp')).toHaveTextContent('2026-07-15 15:00')
    expect(api.getJson).toHaveBeenCalledTimes(2)
  })

  it('refetches when the session changes under a still-mounted gate (token refresh, account switch)', async () => {
    api.getJson
      .mockResolvedValueOnce({ dateFormat: 'dd/MM/yyyy', timezone: 'Europe/Amsterdam' })
      .mockResolvedValueOnce({ dateFormat: 'dd/MM/yyyy', timezone: 'Asia/Tokyo' })

    const view = renderGateAs(user('u-a', 'tenant-a'))
    expect(await screen.findByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')

    // An unrecoverable 401 → refresh → a different subject never passes through logout().
    view.rerender(gateFor(user('u-b', 'tenant-b')))

    await waitFor(() => expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 15:00'))
    expect(api.getJson).toHaveBeenCalledTimes(2)
  })

  it('returns the formatters to their defaults on sign-out', async () => {
    api.getJson.mockResolvedValue({ dateFormat: 'yyyy-MM-dd', timezone: 'Asia/Tokyo' })

    const view = renderGateAs(user('u-a', 'tenant-a'))
    expect(await screen.findByTestId('stamp')).toHaveTextContent('2026-07-15 15:00')
    view.unmount()

    // What AuthContext.logout() calls: nothing of the previous account survives it.
    resetDisplayPreferences()
    render(<Probe />)
    expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')
  })

  it('fetches the preferences once per session, however many shells mount', async () => {
    api.getJson.mockResolvedValue({ dateFormat: 'dd/MM/yyyy', timezone: 'Europe/Amsterdam' })

    renderGate()
    renderGate()

    await screen.findAllByTestId('stamp')
    expect(api.getJson).toHaveBeenCalledTimes(1)
    expect(api.getJson).toHaveBeenCalledWith('/api/company-settings/display')
  })
})
