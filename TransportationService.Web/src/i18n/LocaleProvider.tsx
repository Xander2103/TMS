import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { apiClient } from '../api/apiClient'
import { cacheLocale, readCachedLocale, setActiveLocale } from './activeLocale'
import { formatCurrency, formatDate, formatDateTime } from './formatters'
import { LocaleContext, type LocaleContextValue, type SetLocaleOptions } from './localeContext'
import { detectBrowserLocale, translate, type Locale } from './translations'

interface LocaleProviderProps {
  children: ReactNode
  /**
   * Saved preference (User.PreferredLanguageCode) uit /api/auth/me of de portalcontext.
   * Laadt asynchroon: kan als null starten en later aankomen; tot dan beslist de
   * localStorage-cache (geen taal-flash bij refresh, §51) en anders de browsertaal.
   */
  preferredLanguage?: Locale | null
}

/**
 * DE i18n-provider van de hele applicatie (i18n-wave: voorheen alleen klantportaal +
 * anonieme authpagina's; nu app-root). Resolutie: bewaarde gebruikersvoorkeur →
 * ts.locale-cache → browsertaal → nl; de tenant-default wordt door AppLayout als
 * fallback toegepast zodra de display-voorkeuren binnenkomen en de gebruiker géén
 * eigen voorkeur heeft. Persist gaat naar PUT /api/me/language (intern én portaal —
 * zelfde User-kolom); de wissel zelf is altijd onmiddellijk en client-side (§12).
 */
export function LocaleProvider({ children, preferredLanguage }: LocaleProviderProps) {
  const [locale, setLocaleState] = useState<Locale>(
    () => preferredLanguage ?? readCachedLocale() ?? detectBrowserLocale(),
  )
  const userHasChosen = useRef(false)

  // Adopt the saved preference once it arrives — unless the user already switched
  // manually in this session, in which case their explicit choice wins.
  useEffect(() => {
    if (preferredLanguage && !userHasChosen.current) {
      setLocaleState(preferredLanguage)
    }
  }, [preferredLanguage])

  // Niet-React-helpers (datum-/duurformatters) + <html lang> volgen de actieve taal.
  useEffect(() => {
    setActiveLocale(locale)
    cacheLocale(locale)
  }, [locale])

  const setLocale = useCallback((next: Locale, options?: SetLocaleOptions) => {
    userHasChosen.current = true
    setLocaleState(next)
    if (options?.persist) {
      // Fire-and-forget: the client-side switch must never be blocked by a failing save.
      void apiClient
        .putJson<unknown, { language: Locale }>('/api/me/language', { language: next })
        .catch(() => {})
    }
  }, [])

  /** Tenant-default toepassen zonder de status van "bewuste keuze" te claimen. */
  const applyFallbackLocale = useCallback((next: Locale) => {
    if (!userHasChosen.current) {
      setLocaleState(next)
    }
  }, [])

  const value = useMemo<LocaleContextValue>(
    () => ({
      locale,
      setLocale,
      applyFallbackLocale,
      t: (key, params) => translate(locale, key, params),
      formatDate: (iso) => formatDate(locale, iso),
      formatDateTime: (iso) => formatDateTime(locale, iso),
      formatCurrency: (amount, currency) => formatCurrency(locale, amount, currency),
    }),
    [locale, setLocale, applyFallbackLocale],
  )

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>
}
