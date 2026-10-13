import { createContext, useContext } from 'react'
import type { ResolvedTheme, ThemePreference } from './themePreference'

export interface SetThemePreferenceOptions {
  /** Ook bewaren op de gebruiker via PUT /api/me/theme (alleen zinvol voor ingelogde sessies). */
  persist?: boolean
}

export interface ThemeContextValue {
  /** De gekozen voorkeur (system/light/dark). */
  preference: ThemePreference
  /** Het thema dat effectief op het scherm staat (system is hier al opgelost). */
  resolved: ResolvedTheme
  /**
   * Wisselt onmiddellijk client-side; met `persist` wordt de keuze fire-and-forget bewaard.
   * De promise resolvet na de (eventuele) server-save en rejectt wanneer die mislukt, zodat een
   * kiezer dat kan melden — de lokale wissel zelf wordt nooit teruggedraaid.
   */
  setPreference: (preference: ThemePreference, options?: SetThemePreferenceOptions) => Promise<void>
}

/**
 * Niet-persisterende default: componenten die buiten een ThemeProvider gerenderd worden
 * (unit tests die een pagina direct mounten) krijgen gewoon het lichte thema.
 */
export const DEFAULT_THEME_VALUE: ThemeContextValue = {
  preference: 'system',
  resolved: 'light',
  setPreference: async () => {},
}

export const ThemeContext = createContext<ThemeContextValue>(DEFAULT_THEME_VALUE)

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
