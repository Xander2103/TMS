import { Monitor, Moon, Sun, type LucideIcon } from 'lucide-react'
import { useContext, useId } from 'react'
import { ToastContext } from '../components/ui/toastContext'
import { useLocale } from '../i18n/localeContext'
import { useTheme } from './themeContext'
import { THEME_PREFERENCES, type ThemePreference } from './themePreference'
import './theme-controls.css'

const ICONS: Record<ThemePreference, LucideIcon> = { system: Monitor, light: Sun, dark: Moon }

/**
 * Volledige "Weergave"-instelling voor profiel-/voorkeurenpagina's: een radiogroep
 * Systeem / Licht / Donker met toelichting. Elke keuze past onmiddellijk toe en wordt op de
 * GEBRUIKER bewaard (geen aparte bewaarknop: het is een persoonlijke, direct zichtbare keuze).
 */
export function AppearanceSettings() {
  const { preference, setPreference } = useTheme()
  // Optional on purpose: the switcher must never crash a shell that mounts without toasts.
  const toast = useContext(ToastContext)
  const { t } = useLocale()
  const name = useId()

  const choose = (next: ThemePreference) => {
    void setPreference(next, { persist: true }).catch(() => toast?.showError(t('ui.appearance.saveFailed')))
  }

  return (
    <fieldset className="appearance-settings">
      <legend className="appearance-settings-legend">{t('ui.appearance.label')}</legend>
      <p className="appearance-settings-description">{t('ui.appearance.description')}</p>
      <div className="appearance-options">
        {THEME_PREFERENCES.map((candidate) => {
          const Icon = ICONS[candidate]
          const id = `${name}-${candidate}`
          return (
            <label key={candidate} htmlFor={id} className={candidate === preference ? 'appearance-option is-active' : 'appearance-option'}>
              <input
                id={id}
                type="radio"
                name={name}
                value={candidate}
                checked={candidate === preference}
                onChange={() => choose(candidate)}
              />
              <Icon size={18} aria-hidden="true" className="appearance-option-icon" />
              <span className="appearance-option-text">
                <span className="appearance-option-label">{t(`ui.appearance.${candidate}`)}</span>
                <span className="appearance-option-hint">{t(`ui.appearance.${candidate}Hint`)}</span>
              </span>
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}
