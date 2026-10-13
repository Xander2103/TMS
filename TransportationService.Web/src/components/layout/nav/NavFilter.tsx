import { Search } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'

export function NavFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const { t } = useLocale()
  return (
    <div className="nav-filter">
      <Search className="nav-filter-icon" size={16} aria-hidden />
      <input
        type="search"
        className="nav-filter-input"
        placeholder={`${t('ui.nav.filterMenu')}…`}
        aria-label={t('ui.nav.filterMenu')}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <kbd className="nav-filter-kbd" aria-hidden>⌘K</kbd>
    </div>
  )
}
