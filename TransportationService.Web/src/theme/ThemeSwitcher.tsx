import { Monitor, Moon, Sun, type LucideIcon } from 'lucide-react'
import { useContext } from 'react'
import { ToastContext } from '../components/ui/toastContext'
import { useLocale } from '../i18n/localeContext'
import { useTheme } from './themeContext'
import { THEME_PREFERENCES, type ThemePreference } from './themePreference'
import './theme-controls.css'

const ICONS: Record<ThemePreference, LucideIcon> = { system: Monitor, light: Sun, dark: Moon }

/**
 * Compacte snelkiezer (sidebar-footer naast de taalkiezer): drie iconknoppen voor
 * Systeem / Licht / Donker. Wisselen werkt onmiddellijk client-side en bewaart de keuze op de
 * gebruiker — nooit op de tenant. Dezelfde `aria-pressed`-groep als de portaal-taalkiezer.
 */
export function ThemeSwitcher() {
  const { preference, setPreference } = useTheme()
  // Optional on purpose: the switcher must never crash a shell that mounts without toasts.
  const toast = useContext(ToastContext)
  const { t } = useLocale()

  const choose = (next: ThemePreference) => {
    void setPreference(next, { persist: true }).catch(() => toast?.showError(t('ui.appearance.saveFailed')))
  }

  return (
    <div className="theme-switcher" role="group" aria-label={t('ui.appearance.label')}>
      {THEME_PREFERENCES.map((candidate) => {
        const Icon = ICONS[candidate]
        const label = t(`ui.appearance.${candidate}`)
        return (
          <button
            key={candidate}
            type="button"
            className={candidate === preference ? 'theme-switcher-button is-active' : 'theme-switcher-button'}
            aria-pressed={candidate === preference}
            aria-label={label}
            title={label}
            onClick={() => choose(candidate)}
          >
            <Icon size={15} aria-hidden="true" />
          </button>
        )
      })}
    </div>
  )
}
