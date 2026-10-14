import { createContext, useContext } from 'react'
import type { CurrentUser } from './authTypes'

export type SessionUnavailableReason = 'network' | 'rate_limited' | 'server'

/**
 * Backoff between session-restore attempts when the server cannot be reached (B01). Three
 * retries, ~11 s in total: long enough to ride out a reload storm or a restarting API, short
 * enough that a user with a real outage sees the "unavailable" screen before they give up.
 */
export const SESSION_RETRY_DELAYS_MS: readonly number[] = [1000, 3000, 7000]

export interface AuthContextValue {
  /**
   * `unavailable`: the session could not be restored because the server could not be reached or
   * refused to answer (rate limit, 5xx, network). The credential was NOT rejected, so the user is
   * neither signed in nor signed out until `retrySession()` succeeds or fails for real (B01).
   */
  status: 'loading' | 'authenticated' | 'unauthenticated' | 'unavailable'
  /** Why the session is unavailable (status `unavailable` only). */
  unavailableReason: SessionUnavailableReason | null
  /** One more restore attempt, without backoff; the UI offers it as "Opnieuw proberen". */
  retrySession: () => Promise<void>
  user: CurrentUser | null
  /**
   * True from an explicit `logout()` until the next sign-in. Distinguishes "the user ended the
   * session" from "a visitor/expired session hit a protected page": only the latter returns to
   * the requested page after sign-in — the next sign-in may well be a different account.
   */
  signedOut: boolean
  login: (email: string, password: string, signal?: AbortSignal) => Promise<void>
  logout: () => Promise<void>
  hasPermission: (code: string) => boolean
  hasAnyPermission: (codes: string[]) => boolean
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
