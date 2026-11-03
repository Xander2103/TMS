import { ChevronDown, Globe } from 'lucide-react'
import { useLocale } from '../../i18n/localeContext'
import { LANGUAGE_NAMES } from '../../i18n/languageNames'
import { LOCALES, isLocale } from '../../i18n/translations'
import './app-language-switcher.css'

/**
 * De ene taalkiezer van de interne app (sidebar-footer; het klantportaal houdt zijn
 * eigen knoppenvariant op dezelfde context). Wisselen werkt onmiddellijk client-side
 * en bewaart de voorkeur server-side via PUT /api/me/language — geen logout nodig.
 */
export function AppLanguageSwitcher() {
  const { locale, setLocale, t } = useLocale()

  return (
    <label className="app-lang">
      <Globe className="app-lang-icon" size={16} aria-hidden="true" />
      <span className="app-lang-label">{t('ui.nav.language')}</span>
      <span className="app-lang-control">
        <select
          className="app-lang-select"
          aria-label={t('common.languageSwitcher.label')}
          value={locale}
          onChange={(event) => {
            if (isLocale(event.target.value)) setLocale(event.target.value, { persist: true })
          }}
        >
          {LOCALES.map((candidate) => (
            <option key={candidate} value={candidate} lang={candidate}>
              {LANGUAGE_NAMES[candidate]}
            </option>
          ))}
        </select>
        <ChevronDown className="app-lang-chevron" size={14} aria-hidden="true" />
      </span>
    </label>
  )
}
