import { createContext, useContext } from 'react'
import type { CurrentUser } from './authTypes'

export interface AuthContextValue {
  status: 'loading' | 'authenticated' | 'unauthenticated'
  user: CurrentUser | null
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
