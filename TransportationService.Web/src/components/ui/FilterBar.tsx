import type { ChangeEvent, ReactNode } from 'react'
import { useLocale } from '../../i18n/localeContext'
import './FilterBar.css'

interface FilterBarProps {
  search: string
  onSearchChange: (value: string) => void
  searchPlaceholder?: string
  /** Optional active/inactive tri-state filter. Omit to hide it. */
  activeFilter?: boolean | undefined
  onActiveFilterChange?: (value: boolean | undefined) => void
  /** Extra controls (additional selects, buttons) rendered on the right. */
  children?: ReactNode
  /** B26: shown when anything differs from the default view; returns to a clean URL. */
  onClear?: () => void
  /** Trailing controls pushed to the far right (density toggle, column chooser). */
  trailing?: ReactNode
}

/** Reusable list toolbar: debounce-friendly search input + optional status filter + extras. */
export function FilterBar({
  search,
  onSearchChange,
  searchPlaceholder,
  activeFilter,
  onActiveFilterChange,
  children,
  onClear,
  trailing,
}: FilterBarProps) {
  const { t } = useLocale()
  function handleActiveChange(event: ChangeEvent<HTMLSelectElement>) {
    if (!onActiveFilterChange) return
    const { value } = event.target
    onActiveFilterChange(value === '' ? undefined : value === 'true')
  }

  return (
    <div className="ui-filter-bar">
      <input
        type="search"
        className="ui-filter-search"
        placeholder={searchPlaceholder ?? t('ui.filter.searchPlaceholder')}
        value={search}
        onChange={(event) => onSearchChange(event.target.value)}
        aria-label={t('ui.filter.searchLabel')}
      />
      {onActiveFilterChange && (
        <select
          className="ui-filter-select"
          value={activeFilter === undefined ? '' : String(activeFilter)}
          onChange={handleActiveChange}
          aria-label={t('ui.filter.statusLabel')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          <option value="true">{t('ui.filter.active')}</option>
          <option value="false">{t('ui.filter.inactive')}</option>
        </select>
      )}
      {children}
      {onClear && (
        <button type="button" className="ui-filter-clear" onClick={onClear}>
          {t('ui.filter.clear')}
        </button>
      )}
      {trailing && <div className="ui-filter-trailing">{trailing}</div>}
    </div>
  )
}
