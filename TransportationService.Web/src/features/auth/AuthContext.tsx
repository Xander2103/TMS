import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { setUnauthorizedHandler } from '../../api/apiClient'
import { resetDisplayPreferences } from '../../components/layout/displayPreferences'
import * as authApi from './authApi'
import { AuthContext, SESSION_RETRY_DELAYS_MS, type AuthContextValue, type SessionUnavailableReason } from './authContextValue'
import { clearLegacyTokens, clearTokens, getAccessToken, setAccessToken } from './authStorage'
import type { CurrentUser } from './authTypes'

type AuthStatus = AuthContextValue['status']

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

interface AuthProviderProps {
  children: ReactNode
  /** Test hook: delays between restore attempts (default SESSION_RETRY_DELAYS_MS). */
  retryDelaysMs?: readonly number[]
}

export function AuthProvider({ children, retryDelaysMs = SESSION_RETRY_DELAYS_MS }: AuthProviderProps) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [unavailableReason, setUnavailableReason] = useState<SessionUnavailableReason | null>(null)
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [signedOut, setSignedOut] = useState(false)
  const initialised = useRef(false)

  /**
   * Restore the session from the HttpOnly refresh cookie. A rejected credential means "not
   * signed in"; a server that cannot answer (429 / 5xx / network) is retried with backoff and
   * then surfaced as `unavailable` — the user keeps the chance to recover without re-entering
   * a password, and nothing of a possibly valid session is thrown away.
   */
  const restoreSession = useCallback(async (delays: readonly number[]) => {
    for (let attempt = 0; ; attempt += 1) {
      const outcome = await authApi.refresh()
      if (outcome.status === 'ok') {
        setAccessToken(outcome.tokens.accessToken)
        setUser(outcome.tokens.user)
        setUnavailableReason(null)
        setStatus('authenticated')
        return
      }
      if (outcome.status === 'unauthenticated') {
        clearTokens()
        setUnavailableReason(null)
        setStatus('unauthenticated')
        return
      }
      if (attempt >= delays.length) {
        setUnavailableReason(outcome.reason)
        setStatus('unavailable')
        return
      }
      await sleep(delays[attempt])
    }
  }, [])

  // Register the shared apiClient's "give up" handler so an unrecoverable 401 (refresh failed)
  // drops the app back to the unauthenticated state exactly once, without redirect loops.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      // C-03: an unrecoverable 401 never passes through logout(), so the regional preferences
      // are dropped here too — nothing of this account may colour the next one's screens.
      resetDisplayPreferences()
      setUser(null)
      setStatus('unauthenticated')
    })
    return () => setUnauthorizedHandler(null)
  }, [])

  // Restore the session on load. Runs once (guarded against StrictMode double-invoke) so we never
  // spam the API during initialisation.
  useEffect(() => {
    if (initialised.current) return
    initialised.current = true

    // H5 migration: any tokens the previous architecture left in localStorage are wiped.
    clearLegacyTokens()

    // The session credential is the HttpOnly refresh cookie — ask the server for a fresh
    // access token; no cookie (or an expired one) simply means "not signed in".
    void restoreSession(retryDelaysMs)
  }, [restoreSession, retryDelaysMs])

  // Manual recovery from the "unavailable" screen: one attempt, no backoff (the user decides
  // when to try again; hammering a rate-limited endpoint would only extend the block).
  const retrySession = useCallback(async () => {
    setStatus('loading')
    await restoreSession([])
  }, [restoreSession])

  const login = useCallback(async (email: string, password: string, signal?: AbortSignal) => {
    const tokens = await authApi.login(email, password, signal)
    setAccessToken(tokens.accessToken)
    setUser(tokens.user)
    setSignedOut(false)
    setStatus('authenticated')
  }, [])

  const logout = useCallback(async () => {
    const accessToken = getAccessToken()
    if (accessToken) {
      await authApi.logout(accessToken)
    }
    clearTokens()
    // Secure cleanup: queued offline actions and cached driver snapshots never
    // survive for the next account on this device.
    const { clearDriverOfflineState } = await import('../driver/offlineActions')
    clearDriverOfflineState()
    // C-03: the tenant's date format, decimal separator and TIME ZONE go with it. The cache in
    // DisplayPreferencesProvider is session-keyed as well, so this is belt-and-braces — it is
    // what keeps the *unauthenticated* screens off the previous tenant's notation.
    resetDisplayPreferences()
    setUser(null)
    // Same render as the status flip, so RequireAuth never records the page being left as the
    // place to return to (it did: after a forced password change that was /change-password).
    setSignedOut(true)
    setStatus('unauthenticated')
  }, [])

  const value = useMemo<AuthContextValue>(() => {
    const permissions = new Set(user?.permissions ?? [])
    return {
      status,
      unavailableReason,
      retrySession,
      user,
      signedOut,
      login,
      logout,
      hasPermission: (code: string) => permissions.has(code),
      hasAnyPermission: (codes: string[]) => codes.some((code) => permissions.has(code)),
    }
  }, [status, unavailableReason, retrySession, user, signedOut, login, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
