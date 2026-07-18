import type { ChangeEvent } from 'react'
import './EmployeeFilters.css'

interface EmployeeFiltersProps {
  search: string
  isActive: boolean | undefined
  onSearchChange: (search: string) => void
  onActiveFilterChange: (isActive: boolean | undefined) => void
}

export function EmployeeFilters({ search, isActive, onSearchChange, onActiveFilterChange }: EmployeeFiltersProps) {
  function handleSearchChange(event: ChangeEvent<HTMLInputElement>) {
    onSearchChange(event.target.value)
  }

  function handleActiveChange(event: ChangeEvent<HTMLSelectElement>) {
    const { value } = event.target
    onActiveFilterChange(value === '' ? undefined : value === 'true')
  }

  return (
    <div className="employee-filters">
      <input
        type="search"
        placeholder="Zoeken op nummer, naam of e-mail..."
        value={search}
        onChange={handleSearchChange}
        aria-label="Medewerkers zoeken"
      />
      <select value={isActive === undefined ? '' : String(isActive)} onChange={handleActiveChange} aria-label="Status filter">
        <option value="">Alle statussen</option>
        <option value="true">Actief</option>
        <option value="false">Inactief</option>
      </select>
    </div>
  )
}
