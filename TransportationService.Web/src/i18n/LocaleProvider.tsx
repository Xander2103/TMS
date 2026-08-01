import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { apiClient } from '../api/apiClient'
import { formatCurrency, formatDate, formatDateTime } from './formatters'
import { LocaleContext, type LocaleContextValue, type SetLocaleOptions } from './localeContext'
import { detectBrowserLocale, translate, type Locale } from './translations'

interface LocaleProviderProps {
  children: ReactNode
  /**
   * Saved preference from the portal context (GET /api/customer-portal/context). Loads
   * asynchronously, so it may start as null/undefined and arrive later; until then — and on
   * anonymous pages such as login/activation, which never have a preference — the browser
   * language decides (nl/fr/en prefixes, anything else → Dutch).
   */
  preferredLanguage?: Locale | null
}

/**
 * Lightweight i18n provider for the customer portal (and the anonymous auth pages). The
 * internal app deliberately stays Dutch and never mounts this provider.
 */
export function LocaleProvider({ children, preferredLanguage }: LocaleProviderProps) {
  const [locale, setLocaleState] = useState<Locale>(() => preferredLanguage ?? detectBrowserLocale())
  const userHasChosen = useRef(false)

  // Adopt the saved preference once the portal context arrives — unless the user already
  // switched manually in this session, in which case their explicit choice wins.
  useEffect(() => {
    if (preferredLanguage && !userHasChosen.current) {
      setLocaleState(preferredLanguage)
    }
  }, [preferredLanguage])

  const setLocale = useCallback((next: Locale, options?: SetLocaleOptions) => {
    userHasChosen.current = true
    setLocaleState(next)
    if (options?.persist) {
      // Fire-and-forget: the client-side switch must never be blocked by a failing save.
      void apiClient
        .putJson<unknown, { language: Locale }>('/api/customer-portal/profile/language', { language: next })
        .catch(() => {})
    }
  }, [])

  const value = useMemo<LocaleContextValue>(
    () => ({
      locale,
      setLocale,
      t: (key, params) => translate(locale, key, params),
      formatDate: (iso) => formatDate(locale, iso),
      formatDateTime: (iso) => formatDateTime(locale, iso),
      formatCurrency: (amount, currency) => formatCurrency(locale, amount, currency),
    }),
    [locale, setLocale],
  )

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>
}
