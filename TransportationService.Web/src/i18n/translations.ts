import nlCommon from '../locales/nl/common.json'
import nlNavigation from '../locales/nl/navigation.json'
import nlAuth from '../locales/nl/auth.json'
import nlDashboard from '../locales/nl/dashboard.json'
import nlOrders from '../locales/nl/orders.json'
import nlInvoices from '../locales/nl/invoices.json'
import nlDocuments from '../locales/nl/documents.json'
import nlNotifications from '../locales/nl/notifications.json'
import nlMessages from '../locales/nl/messages.json'
import nlErrors from '../locales/nl/errors.json'
import frCommon from '../locales/fr/common.json'
import frNavigation from '../locales/fr/navigation.json'
import frAuth from '../locales/fr/auth.json'
import frDashboard from '../locales/fr/dashboard.json'
import frOrders from '../locales/fr/orders.json'
import frInvoices from '../locales/fr/invoices.json'
import frDocuments from '../locales/fr/documents.json'
import frNotifications from '../locales/fr/notifications.json'
import frMessages from '../locales/fr/messages.json'
import frErrors from '../locales/fr/errors.json'
import enCommon from '../locales/en/common.json'
import enNavigation from '../locales/en/navigation.json'
import enAuth from '../locales/en/auth.json'
import enDashboard from '../locales/en/dashboard.json'
import enOrders from '../locales/en/orders.json'
import enInvoices from '../locales/en/invoices.json'
import enDocuments from '../locales/en/documents.json'
import enNotifications from '../locales/en/notifications.json'
import enMessages from '../locales/en/messages.json'
import enErrors from '../locales/en/errors.json'

export type Locale = 'nl' | 'fr' | 'en'

export const LOCALES: readonly Locale[] = ['nl', 'fr', 'en']

export function isLocale(value: unknown): value is Locale {
  return value === 'nl' || value === 'fr' || value === 'en'
}

/** Nested bundle of translation strings, keyed by domain then by (possibly nested) key. */
export interface MessageTree {
  [key: string]: string | MessageTree
}

export type MessageBundle = Record<Locale, MessageTree>

/**
 * All portal UI strings, statically imported so vite bundles (and tree-checks) them at build
 * time. Dutch is the source of truth; the completeness test enforces identical key sets.
 */
export const MESSAGES: MessageBundle = {
  nl: {
    common: nlCommon,
    navigation: nlNavigation,
    auth: nlAuth,
    dashboard: nlDashboard,
    orders: nlOrders,
    invoices: nlInvoices,
    documents: nlDocuments,
    notifications: nlNotifications,
    messages: nlMessages,
    errors: nlErrors,
  },
  fr: {
    common: frCommon,
    navigation: frNavigation,
    auth: frAuth,
    dashboard: frDashboard,
    orders: frOrders,
    invoices: frInvoices,
    documents: frDocuments,
    notifications: frNotifications,
    messages: frMessages,
    errors: frErrors,
  },
  en: {
    common: enCommon,
    navigation: enNavigation,
    auth: enAuth,
    dashboard: enDashboard,
    orders: enOrders,
    invoices: enInvoices,
    documents: enDocuments,
    notifications: enNotifications,
    messages: enMessages,
    errors: enErrors,
  },
}

function resolve(tree: MessageTree, key: string): string | undefined {
  let node: string | MessageTree | undefined = tree
  for (const part of key.split('.')) {
    if (node === undefined || typeof node === 'string') return undefined
    node = node[part]
  }
  return typeof node === 'string' ? node : undefined
}

function interpolate(template: string, params?: Record<string, string | number>): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match))
}

/**
 * Core lookup, parameterised on the bundle for testability: chosen locale → Dutch fallback →
 * the key itself (with a dev-only warning, so missing keys surface during development without
 * ever breaking the UI for a customer).
 */
export function translateFrom(
  bundle: MessageBundle,
  locale: Locale,
  key: string,
  params?: Record<string, string | number>,
): string {
  const value = resolve(bundle[locale], key) ?? resolve(bundle.nl, key)
  if (value === undefined) {
    if (import.meta.env.DEV) {
      console.warn(`[i18n] Ontbrekende vertaalsleutel "${key}" (taal "${locale}")`)
    }
    return key
  }
  return interpolate(value, params)
}

export function translate(locale: Locale, key: string, params?: Record<string, string | number>): string {
  return translateFrom(MESSAGES, locale, key, params)
}

/** Browser languages with an nl/fr/en prefix map onto a portal locale; anything else falls back to Dutch. */
export function detectBrowserLocale(): Locale {
  const language = typeof navigator !== 'undefined' ? (navigator.language ?? '') : ''
  const lower = language.toLowerCase()
  if (lower.startsWith('fr')) return 'fr'
  if (lower.startsWith('en')) return 'en'
  return 'nl'
}
