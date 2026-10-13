import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { apiClient } from '../api/apiClient'
import { ThemeContext, type SetThemePreferenceOptions, type ThemeContextValue } from './themeContext'
import {
  applyResolvedTheme, cacheThemePreference, readCachedThemePreference, resolveTheme, systemPrefersDark,
  watchSystemTheme, type ThemePreference,
} from './themePreference'

interface ThemeProviderProps {
  children: ReactNode
  /**
   * Bewaarde voorkeur van de ingelogde gebruiker (CurrentUser.themePreference). Laadt
   * asynchroon: null/undefined zolang de sessie niet geladen is of de gebruiker nooit koos.
   */
  savedPreference?: ThemePreference | null
  /**
   * Identiteit van de sessie waar `savedPreference` bij hoort (user-id), null voor een anonieme
   * of ladende sessie. Een andere sessie betekent een ANDERE gebruiker: diens bewaarde keuze
   * wint dan altijd, ook over een wissel die de vorige gebruiker in deze tab nog maakte.
   */
  sessionId?: string | null
}

const ANONYMOUS = 'anonymous'

/**
 * DE themaprovider van de hele applicatie (app-root, boven RequireAuth, zodat ook de
 * loginpagina het juiste thema draagt). Resolutie per sessie:
 *
 *   ingelogd  → bewaarde gebruikersvoorkeur (bron van waarheid) → 'system' als die nooit koos
 *   anoniem   → laatst toegepaste keuze op dit toestel (ts.theme-cache) → 'system'
 *
 * Een wissel in deze tab geldt onmiddellijk voor de huidige sessie en wordt (met `persist`)
 * op de gebruiker bewaard; de cache volgt altijd het toegepaste thema zodat de volgende
 * start zonder flash gebeurt. De cache is nooit de bron van waarheid voor een ingelogde
 * gebruiker: op een ander toestel komt dezelfde keuze gewoon uit /api/auth/me.
 */
export function ThemeProvider({ children, savedPreference = null, sessionId = null }: ThemeProviderProps) {
  const sessionKey = sessionId ?? ANONYMOUS
  // Een expliciete wissel in deze tab, gekoppeld aan de sessie waarin ze gebeurde.
  const [override, setOverride] = useState<{ sessionKey: string; preference: ThemePreference } | null>(null)
  const [systemDark, setSystemDark] = useState<boolean>(systemPrefersDark)

  const preference: ThemePreference = useMemo(() => {
    if (override?.sessionKey === sessionKey) return override.preference
    if (sessionId) return savedPreference ?? 'system'
    // Anonieme of ladende sessie: de laatst toegepaste keuze van dit toestel. Synchroon gelezen
    // bij elke sessiewissel, zodat ook het loginscherm na een sign-out het vertrouwde thema houdt.
    return readCachedThemePreference() ?? 'system'
  }, [override, sessionKey, sessionId, savedPreference])

  const resolved = resolveTheme(preference, systemDark)

  useEffect(() => watchSystemTheme(setSystemDark), [])

  // Geen layout-effect nodig: het inline script in index.html heeft het gecachete thema al
  // vóór de eerste paint gezet; dit effect corrigeert enkel wanneer de sessie iets anders zegt.
  useEffect(() => {
    applyResolvedTheme(resolved)
    cacheThemePreference(preference)
  }, [resolved, preference])

  const setPreference = useCallback(
    async (next: ThemePreference, options?: SetThemePreferenceOptions) => {
      setOverride({ sessionKey, preference: next })
      if (!options?.persist || !sessionId) return
      // De server is de bron van waarheid voor ingelogde gebruikers; de lokale wissel blijft
      // hoe dan ook staan — een mislukte save mag het scherm nooit terugzetten.
      await apiClient.putJson<unknown, { theme: ThemePreference }>('/api/me/theme', { theme: next })
    },
    [sessionKey, sessionId],
  )

  const value = useMemo<ThemeContextValue>(
    () => ({ preference, resolved, setPreference }),
    [preference, resolved, setPreference],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
