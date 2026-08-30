import { useEffect, useState, type ReactNode } from 'react'
import { apiClient } from '../../api/apiClient'
import { setDateFormatPreference, setTimeZonePreference } from '../../utils/dates'
import { setDecimalSeparatorPreference } from '../../utils/numbers'

/**
 * THE regional-preferences bootstrap of the application — one implementation shared by all
 * THREE authenticated shells (AppLayout, CustomerPortalLayout, DriverLayout). Every shell needs
 * it and none of them may own a private copy: the driver app used to have none at all, so its
 * screens silently rendered every timestamp in the seeded default zone.
 *
 * Two properties matter, and both were missing before (C-03 review I-1/I-2):
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
 *
 * The fetch is memoised per session, so a user with both internal and portal access pays for it
 * once no matter how many shells mount.
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

let inFlight: Promise<DisplayPreferences> | null = null

/**
 * Fetches the tenant's regional preferences once and applies them to the module-level
 * formatters. Never rejects: a failure yields the empty set, which leaves the defaults in place.
 */
export function loadDisplayPreferences(): Promise<DisplayPreferences> {
  inFlight ??= apiClient
    .getJson<DisplayPreferences>('/api/company-settings/display')
    .catch((): DisplayPreferences => ({}))
    .then((preferences) => {
      setDateFormatPreference(preferences.dateFormat)
      setDecimalSeparatorPreference(preferences.decimalSeparator)
      setTimeZonePreference(preferences.timezone)
      return preferences
    })
  return inFlight
}

/** Test-only escape hatch: drops the session memo so each case starts from a clean fetch. */
export function resetDisplayPreferencesForTests(): void {
  inFlight = null
}

export interface DisplayPreferencesState {
  /** True once the preferences are applied — or once waiting for them stopped being worth it. */
  ready: boolean
  /** Null until the answer arrives; the empty object when the fetch failed. */
  preferences: DisplayPreferences | null
}

/**
 * Subscribes a component to the bootstrap. Shells use it for the pieces they own themselves
 * (AppLayout applies `defaultLanguage` as the locale fallback); the gate below uses it to decide
 * when the routed content may render.
 */
export function useDisplayPreferences(): DisplayPreferencesState {
  const [preferences, setPreferences] = useState<DisplayPreferences | null>(null)
  const [waitedLongEnough, setWaitedLongEnough] = useState(false)

  useEffect(() => {
    let mounted = true
    void loadDisplayPreferences().then((loaded) => {
      if (mounted) setPreferences(loaded)
    })
    const timer = window.setTimeout(() => {
      if (mounted) setWaitedLongEnough(true)
    }, BOOTSTRAP_TIMEOUT_MS)
    return () => {
      mounted = false
      window.clearTimeout(timer)
    }
  }, [])

  return { ready: preferences !== null || waitedLongEnough, preferences }
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
