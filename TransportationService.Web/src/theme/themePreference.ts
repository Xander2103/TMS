/**
 * Persoonlijke weergavevoorkeur (licht / donker / systeem) — het niet-React-deel.
 *
 * Bron van waarheid is de GEBRUIKER (User.ThemePreference, meegeleverd in /api/auth/me en de
 * login/refresh-respons; geschreven via PUT /api/me/theme). De localStorage-cache hieronder
 * dient uitsluitend om bij het opstarten de laatst toegepaste keuze te kennen VOOR de sessie
 * geladen is (geen flash van het verkeerde thema): het inline script in index.html leest
 * dezelfde sleutel. Zodra de ingelogde gebruiker bekend is, wint diens bewaarde voorkeur en
 * wordt de cache overschreven — de keuze reist zo mee naar elk ander toestel.
 *
 * Het thema is presentatie-only en hoort nooit bij tenant, bedrijf, rol of werkplek.
 */

export type ThemePreference = 'system' | 'light' | 'dark'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_PREFERENCES: readonly ThemePreference[] = ['system', 'light', 'dark']

/** Moet gelijk blijven aan de sleutel in het inline bootstrap-script van index.html. */
export const THEME_STORAGE_KEY = 'ts.theme'

const DARK_SCHEME_QUERY = '(prefers-color-scheme: dark)'

/** Browser-chroomkleur (PWA/mobiel) per thema; licht houdt de bestaande merkkleur uit index.html. */
const DARK_THEME_COLOR = '#0b1220'

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark'
}

/** Laatst toegepaste voorkeur op dit toestel; null wanneer er (nog) niets gecachet is. */
export function readCachedThemePreference(): ThemePreference | null {
  try {
    const value = localStorage.getItem(THEME_STORAGE_KEY)
    return isThemePreference(value) ? value : null
  } catch {
    return null
  }
}

export function cacheThemePreference(preference: ThemePreference): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, preference)
  } catch {
    /* cache is best-effort (privémodus, uitgeschakelde opslag, …) */
  }
}

function darkSchemeQuery(): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return null
  return window.matchMedia(DARK_SCHEME_QUERY)
}

/** Volgt het besturingssysteem; false wanneer matchMedia ontbreekt (tests, oude engines). */
export function systemPrefersDark(): boolean {
  return darkSchemeQuery()?.matches ?? false
}

/** Abonneert op wijzigingen van het systeemthema; geeft de unsubscribe terug. */
export function watchSystemTheme(onChange: (dark: boolean) => void): () => void {
  const query = darkSchemeQuery()
  if (!query) return () => {}
  const listener = (event: MediaQueryListEvent) => onChange(event.matches)
  query.addEventListener('change', listener)
  return () => query.removeEventListener('change', listener)
}

export function resolveTheme(preference: ThemePreference, systemDark: boolean = systemPrefersDark()): ResolvedTheme {
  if (preference === 'light' || preference === 'dark') return preference
  return systemDark ? 'dark' : 'light'
}

/**
 * Zet het opgeloste thema op het documentelement. De tokens in styles/global.css hangen aan
 * `html[data-theme]`; `color-scheme` laat native controls (scrollbars, date pickers, selects)
 * meekleuren. Idempotent, dus veilig om bij elke render-cyclus opnieuw aan te roepen.
 */
export function applyResolvedTheme(theme: ResolvedTheme): void {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  if (root.dataset.theme !== theme) root.dataset.theme = theme
  root.style.colorScheme = theme

  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta) {
    if (!meta.dataset.lightContent) meta.dataset.lightContent = meta.content
    meta.content = theme === 'dark' ? DARK_THEME_COLOR : meta.dataset.lightContent
  }
}

/** Huidig toegepaste thema volgens het document (null vóór de eerste toepassing). */
export function readAppliedTheme(): ResolvedTheme | null {
  if (typeof document === 'undefined') return null
  const value = document.documentElement.dataset.theme
  return value === 'light' || value === 'dark' ? value : null
}
