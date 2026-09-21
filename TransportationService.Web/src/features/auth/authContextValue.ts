import { createContext, useContext } from 'react'
import type { CurrentUser } from './authTypes'

export interface AuthContextValue {
  status: 'loading' | 'authenticated' | 'unauthenticated'
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
