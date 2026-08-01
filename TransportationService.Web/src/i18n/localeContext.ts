import { createContext, useContext } from 'react'
import { formatCurrency, formatDate, formatDateTime } from './formatters'
import { translate, type Locale } from './translations'

export interface SetLocaleOptions {
  /** When true the choice is also saved on the portal user via PUT /profile/language. */
  persist?: boolean
}

export interface LocaleContextValue {
  locale: Locale
  setLocale: (locale: Locale, options?: SetLocaleOptions) => void
  t: (key: string, params?: Record<string, string | number>) => string
  formatDate: (iso: string) => string
  formatDateTime: (iso: string) => string
  formatCurrency: (value: number, currency?: string) => string
}

export type TranslateFn = LocaleContextValue['t']

/**
 * Dutch, non-persisting default. Rendering portal building blocks outside a LocaleProvider
 * (internal screens, unit tests that mount a page directly) therefore produces exactly the
 * pre-i18n Dutch UI instead of crashing — the provider only ever *adds* languages.
 */
export const DEFAULT_LOCALE_VALUE: LocaleContextValue = {
  locale: 'nl',
  setLocale: () => {},
  t: (key, params) => translate('nl', key, params),
  formatDate: (iso) => formatDate('nl', iso),
  formatDateTime: (iso) => formatDateTime('nl', iso),
  formatCurrency: (value, currency) => formatCurrency('nl', value, currency),
}

export const LocaleContext = createContext<LocaleContextValue>(DEFAULT_LOCALE_VALUE)

export function useLocale(): LocaleContextValue {
  return useContext(LocaleContext)
}
