import type { ChangeEvent, ReactNode } from 'react'
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
}

/** Reusable list toolbar: debounce-friendly search input + optional status filter + extras. */
export function FilterBar({
  search,
  onSearchChange,
  searchPlaceholder = 'Zoeken...',
  activeFilter,
  onActiveFilterChange,
  children,
}: FilterBarProps) {
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
        placeholder={searchPlaceholder}
        value={search}
        onChange={(event) => onSearchChange(event.target.value)}
        aria-label="Zoeken"
      />
      {onActiveFilterChange && (
        <select
          className="ui-filter-select"
          value={activeFilter === undefined ? '' : String(activeFilter)}
          onChange={handleActiveChange}
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          <option value="true">Actief</option>
          <option value="false">Inactief</option>
        </select>
      )}
      {children}
    </div>
  )
}
