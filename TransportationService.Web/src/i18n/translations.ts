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
 * ALL app UI strings. Namespaces are auto-discovered from `src/locales/<locale>/<domain>.json`
 * via Vite's eager glob, so adding a domain = adding three JSON files — no central registry
 * to edit (and no merge conflicts between module migrations). Dutch is the source of truth;
 * the completeness test enforces identical key sets across nl/fr/en for every domain.
 */
const localeModules = import.meta.glob('../locales/*/*.json', { eager: true }) as Record<
  string,
  { default: MessageTree }
>

function buildMessages(): MessageBundle {
  const bundle: MessageBundle = { nl: {}, fr: {}, en: {} }
  for (const [path, module] of Object.entries(localeModules)) {
    // Path shape: ../locales/<locale>/<domain>.json
    const match = path.match(/\/locales\/(nl|fr|en)\/([\w-]+)\.json$/)
    if (!match) continue
    const [, locale, domain] = match
    bundle[locale as Locale][domain] = module.default
  }
  return bundle
}

export const MESSAGES: MessageBundle = buildMessages()

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
 *
 * Pluralisation (§37): when `params.count` is a number, `key_one` (count === 1) or
 * `key_other` is tried first and the bare key is the fallback. nl/fr/en all share the
 * one/other rule, so CLDR machinery would be dead weight here. NOTE — French treats 0 as
 * singular in prose ("0 employé"); we deliberately follow the simpler one/other split (0 →
 * other) app-wide for consistency across the three languages.
 */
export function translateFrom(
  bundle: MessageBundle,
  locale: Locale,
  key: string,
  params?: Record<string, string | number>,
): string {
  let value: string | undefined
  if (params && typeof params.count === 'number') {
    const pluralKey = `${key}_${params.count === 1 ? 'one' : 'other'}`
    value = resolve(bundle[locale], pluralKey) ?? resolve(bundle.nl, pluralKey)
  }
  value ??= resolve(bundle[locale], key) ?? resolve(bundle.nl, key)
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

/** Browser languages with an nl/fr/en prefix map onto a locale; anything else falls back to Dutch. */
export function detectBrowserLocale(): Locale {
  const language = typeof navigator !== 'undefined' ? (navigator.language ?? '') : ''
  const lower = language.toLowerCase()
  if (lower.startsWith('fr')) return 'fr'
  if (lower.startsWith('en')) return 'en'
  return 'nl'
}
