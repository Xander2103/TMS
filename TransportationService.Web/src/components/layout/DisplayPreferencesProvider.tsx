import { type ReactNode } from 'react'
import { useDisplayPreferences } from './displayPreferences'

/**
 * Gate component of the regional-preferences bootstrap. All non-component pieces (types, the
 * session-keyed cache, load/reset and the useDisplayPreferences hook) live in
 * `displayPreferences.ts` — see the rationale there (react-refresh: a component file exports
 * only components).
 */

interface DisplayPreferencesProviderProps {
  children: ReactNode
  /** Shown while the preferences are on their way; the shell chrome around it stays visible. */
  fallback?: ReactNode
}

/** Wraps the routed content of a shell so no page renders a timestamp on an unknown zone. */
export function DisplayPreferencesProvider({ children, fallback = null }: DisplayPreferencesProviderProps) {
  const { ready } = useDisplayPreferences()
  return <>{ready ? children : fallback}</>
}
