import type { Locale } from './translations'

/**
 * Module-level actieve locale voor NIET-React-helpers (utils/dates.ts weekdag-/maand-
 * namen, duurnotatie, getalformatters). De LocaleProvider is de enige schrijver; alles
 * wat binnen React leeft gebruikt gewoon useLocale(). Zelfde patroon als de
 * datumvoorkeur in utils/dates.ts: één keer per sessie gezet, synchroon leesbaar.
 */

const STORAGE_KEY = 'ts.locale'

let activeLocale: Locale = 'nl'

export function getActiveLocale(): Locale {
  return activeLocale
}

export function setActiveLocale(locale: Locale): void {
  activeLocale = locale
  if (typeof document !== 'undefined') {
    // Toegankelijkheid (§40/§41): de accessibility tree volgt de gekozen taal.
    document.documentElement.lang = locale
  }
}

/** Sessieoverschrijdende cache tegen taal-flash vóór /api/auth/me geladen is (§51). */
export function readCachedLocale(): Locale | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return value === 'nl' || value === 'fr' || value === 'en' ? value : null
  } catch {
    return null
  }
}

export function cacheLocale(locale: Locale): void {
  try {
    localStorage.setItem(STORAGE_KEY, locale)
  } catch {
    /* cache is best-effort */
  }
}

/** Alleen voor tests. */
export function resetActiveLocaleForTests(): void {
  activeLocale = 'nl'
}
