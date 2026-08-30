import { useContext, useEffect, useState, type ReactNode } from 'react'
import { apiClient } from '../../api/apiClient'
import { AuthContext } from '../../features/auth/authContextValue'
import {
  resetDateFormatPreference, resetTimeZonePreference,
  setDateFormatPreference, setTimeZonePreference,
} from '../../utils/dates'
import { resetDecimalSeparatorPreference, setDecimalSeparatorPreference } from '../../utils/numbers'

/**
 * THE regional-preferences bootstrap of the application — one implementation shared by all
 * THREE authenticated shells (AppLayout, CustomerPortalLayout, DriverLayout). Every shell needs
 * it and none of them may own a private copy: the driver app used to have none at all, so its
 * screens silently rendered every timestamp in the seeded default zone.
 *
 * Three properties matter, and all three were missing before (C-03 review I-1/I-2 and the
 * re-review's session-cache finding):
 *
 *  1. The preferences must be known BEFORE a routed page renders. The formatters in
 *     `utils/dates.ts` keep module-level state that no component subscribes to, so a page whose
 *     own data fetch resolved first used to render its windows in the default zone and never
 *     correct itself — a race, not a flash. `DisplayPreferencesProvider` therefore holds the
 *     routed content back until the answer (or a failure) is in.
 *  2. Nothing may block the app. The fetch never rejects — a failure resolves to the empty
 *     preference set, i.e. the built-in defaults, which equal the backend's seeded ones — and a
 *     request that simply hangs opens the gate after BOOTSTRAP_TIMEOUT_MS anyway. An answer that
 *     arrives after that is still applied to the formatters, so everything rendered from then on
 *     is right; content already on screen is deliberately NOT remounted, because throwing away a
 *     half-typed form to correct a clock is the worse trade.
 *  3. The cache belongs to ONE session. Deduping the fetch across shells is only safe while the
 *     signed-in session is the same one: `logout()` and `login()` are pure SPA transitions with
 *     no page reload, so a naive module-level memo would silently re-apply the first tenant's
 *     zone, date format and decimal separator to the second tenant's screens. The cache is
 *     therefore KEYED on the session identity (tenant + user), which also covers the paths that
 *     never pass through `logout()` at all — a failed refresh handled by the apiClient's
 *     unauthorized handler, or a re-login as a different subject. `resetDisplayPreferences()`
 *     from the auth layer is belt-and-braces on top of that, not the mechanism.
 */

export interface DisplayPreferences {
  dateFormat?: string
  decimalSeparator?: string
  /** IANA zone id of the tenant (TenantSettings.Timezone); drives every wall-clock rendering. */
  timezone?: string
  defaultLanguage?: string
}

/** How long the routed content waits before it gives up and renders on the defaults. */
const BOOTSTRAP_TIMEOUT_MS = 3_000

/**
 * Session key used when the gate is rendered outside an AuthProvider (isolated tests) or before
 * the user has resolved. It is a key like any other: the moment a real user arrives the key
 * changes and the preferences are fetched for that session.
 */
const NO_SESSION = 'anonymous'

let cache: { sessionKey: string; preferences: Promise<DisplayPreferences> } | null = null

/** Everything the bootstrap owns, back to the built-in defaults. */
function applyDefaults(): void {
  resetDateFormatPreference()
  resetTimeZonePreference()
  resetDecimalSeparatorPreference()
}

function apply(preferences: DisplayPreferences): void {
  setDateFormatPreference(preferences.dateFormat)
  setDecimalSeparatorPreference(preferences.decimalSeparator)
  setTimeZonePreference(preferences.timezone)
}

/**
 * Fetches the tenant's regional preferences once per SESSION and applies them to the
 * module-level formatters. Never rejects: a failure yields the empty set, which leaves the
 * defaults in place. Repeated calls with the same session key share one request — which is what
 * keeps the three shells (and React StrictMode's double-invoked effects) to a single fetch.
 */
export function loadDisplayPreferences(sessionKey: string): Promise<DisplayPreferences> {
  if (cache?.sessionKey === sessionKey) return cache.preferences

  // A different session is a different tenant's settings. Drop what the previous one applied
  // BEFORE the fetch starts, so its zone cannot leak into the new session's first renders.
  applyDefaults()
  const preferences = apiClient
    .getJson<DisplayPreferences>('/api/company-settings/display')
    .catch((): DisplayPreferences => ({}))
    .then((loaded) => {
      // Only the newest session may write the formatters: a slow response for a session the user
      // has already left must not overwrite the one they are in now.
      if (cache?.sessionKey === sessionKey) apply(loaded)
      return loaded
    })
  cache = { sessionKey, preferences }
  return preferences
}

/**
 * Drops the cached preferences and returns the formatters to their defaults. Called by the auth
 * layer on sign-out and on an unrecoverable 401, so nothing of one account survives into the
 * next on the same tab; also the reset seam for tests.
 */
export function resetDisplayPreferences(): void {
  cache = null
  applyDefaults()
}

/** Tenant + user: either changing means a different set of regional preferences. */
function useSessionKey(): string {
  const auth = useContext(AuthContext)
  const user = auth?.user
  return user ? `${user.tenantId}:${user.id}` : NO_SESSION
}

export interface DisplayPreferencesState {
  /** True once the preferences are applied — or once waiting for them stopped being worth it. */
  ready: boolean
  /** Null until this session's answer arrives; the empty object when the fetch failed. */
  preferences: DisplayPreferences | null
}

/**
 * Subscribes a component to the bootstrap. Shells use it for the pieces they own themselves
 * (AppLayout applies `defaultLanguage` as the locale fallback); the gate below uses it to decide
 * when the routed content may render. Everything is scoped to the current session key, so a
 * session change closes the gate again instead of showing the previous tenant's clock.
 */
export function useDisplayPreferences(): DisplayPreferencesState {
  const sessionKey = useSessionKey()
  const [loaded, setLoaded] = useState<{ sessionKey: string; preferences: DisplayPreferences } | null>(null)
  const [waitedOut, setWaitedOut] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    void loadDisplayPreferences(sessionKey).then((preferences) => {
      if (mounted) setLoaded({ sessionKey, preferences })
    })
    const timer = window.setTimeout(() => {
      if (mounted) setWaitedOut(sessionKey)
    }, BOOTSTRAP_TIMEOUT_MS)
    return () => {
      mounted = false
      window.clearTimeout(timer)
    }
  }, [sessionKey])

  const preferences = loaded?.sessionKey === sessionKey ? loaded.preferences : null
  return { ready: preferences !== null || waitedOut === sessionKey, preferences }
}

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
