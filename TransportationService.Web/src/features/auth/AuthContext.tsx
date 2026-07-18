import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { setUnauthorizedHandler } from '../../api/apiClient'
import * as authApi from './authApi'
import { clearTokens, getAccessToken, getRefreshToken, storeTokens } from './authStorage'
import type { CurrentUser } from './authTypes'

type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated'

interface AuthContextValue {
  status: AuthStatus
  user: CurrentUser | null
  login: (email: string, password: string, signal?: AbortSignal) => Promise<void>
  logout: () => Promise<void>
  hasPermission: (code: string) => boolean
  hasAnyPermission: (codes: string[]) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const initialised = useRef(false)

  // Register the shared apiClient's "give up" handler so an unrecoverable 401 (refresh failed)
  // drops the app back to the unauthenticated state exactly once, without redirect loops.
  useEffect(() => {
    setUnauthorizedHandler(() => {
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

    void (async () => {
      const refreshToken = getRefreshToken()
      if (!refreshToken) {
        setStatus('unauthenticated')
        return
      }

      // Prefer a rotation-refresh so a stale access token is replaced up front.
      const tokens = await authApi.refresh(refreshToken)
      if (tokens) {
        storeTokens(tokens)
        setUser(tokens.user)
        setStatus('authenticated')
        return
      }

      // Refresh token no longer valid — fall back to the access token if one is still good.
      const accessToken = getAccessToken()
      const me = accessToken ? await authApi.fetchCurrentUser(accessToken) : null
      if (me) {
        setUser(me)
        setStatus('authenticated')
      } else {
        clearTokens()
        setStatus('unauthenticated')
      }
    })()
  }, [])

  const login = useCallback(async (email: string, password: string, signal?: AbortSignal) => {
    const tokens = await authApi.login(email, password, signal)
    storeTokens(tokens)
    setUser(tokens.user)
    setStatus('authenticated')
  }, [])

  const logout = useCallback(async () => {
    const accessToken = getAccessToken()
    const refreshToken = getRefreshToken()
    if (accessToken) {
      await authApi.logout(accessToken, refreshToken)
    }
    clearTokens()
    setUser(null)
    setStatus('unauthenticated')
  }, [])

  const value = useMemo<AuthContextValue>(() => {
    const permissions = new Set(user?.permissions ?? [])
    return {
      status,
      user,
      login,
      logout,
      hasPermission: (code: string) => permissions.has(code),
      hasAnyPermission: (codes: string[]) => codes.some((code) => permissions.has(code)),
    }
  }, [status, user, login, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
