import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { EmployeesTable } from '../components/EmployeesTable'
import { EmployeeFilters } from '../components/EmployeeFilters'
import { Pagination } from '../components/Pagination'
import { useEmployees } from '../hooks/useEmployees'

export function EmployeesPage() {
  const [search, setSearch] = useState('')
  const [isActive, setIsActive] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error } = useEmployees({ search, isActive, page })

  function handleSearchChange(value: string) {
    setSearch(value)
    setPage(1)
  }

  function handleActiveFilterChange(value: boolean | undefined) {
    setIsActive(value)
    setPage(1)
  }

  return (
    <>
      <PageHeader
        title="Personeel"
        action={
          <Link to="/employees/new" className="primary-button">
            Nieuwe medewerker
          </Link>
        }
      />

      <EmployeeFilters
        search={search}
        isActive={isActive}
        onSearchChange={handleSearchChange}
        onActiveFilterChange={handleActiveFilterChange}
      />

      {isLoading && <LoadingState message="Medewerkers laden..." />}
      {error && <ErrorState message={error} />}
      {!isLoading && !error && (
        <>
          <EmployeesTable employees={items} />
          <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
        </>
      )}
    </>
  )
}
