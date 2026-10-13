import { useLocale } from '../../../i18n/localeContext'
import { LOCALES, type Locale } from '../../../i18n/translations'
import './language-switcher.css'

/** Endonyms on purpose: every visitor must be able to find their own language. */
const LANGUAGE_NAMES: Record<Locale, string> = {
  nl: 'Nederlands',
  fr: 'Français',
  en: 'English',
}

/**
 * Portal language switcher (bottom-left in the portal shell). Switching applies instantly
 * client-side and persists the preference on the portal user via PUT /profile/language.
 */
export function LanguageSwitcher() {
  const { locale, setLocale, t } = useLocale()

  return (
    <div className="cpl-lang" role="group" aria-label={t('common.languageSwitcher.label')}>
      {LOCALES.map((candidate) => (
        <button
          key={candidate}
          type="button"
          lang={candidate}
          className={candidate === locale ? 'cpl-lang-button cpl-lang-active' : 'cpl-lang-button'}
          aria-pressed={candidate === locale}
          onClick={() => setLocale(candidate, { persist: true })}
        >
          {LANGUAGE_NAMES[candidate]}
        </button>
      ))}
    </div>
  )
}
