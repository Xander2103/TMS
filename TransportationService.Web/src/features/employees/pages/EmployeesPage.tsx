import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { searchEmployees } from '../api/employeesApi'
import {
  EMPLOYMENT_STATUS_LABELS,
  EMPLOYMENT_STATUS_TONES,
  type EmployeeListItem,
  type EmploymentStatus,
} from '../types/employee'
import './employees-page.css'

export function EmployeesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [jobFunctionId, setJobFunctionId] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [employmentStatus, setEmploymentStatus] = useState<EmploymentStatus | ''>('')
  const [page, setPage] = useState(1)

  const jobFunctions = useLookupOptions('/api/job-functions')
  const departments = useLookupOptions('/api/departments')

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<EmployeeListItem>(
    (args) =>
      searchEmployees({
        ...args,
        jobFunctionId: jobFunctionId || undefined,
        departmentId: departmentId || undefined,
        employmentStatus: employmentStatus || undefined,
      }),
    { search, isActive: activeFilter, page, errorMessage: 'Medewerkers konden niet worden geladen.' },
  )

  // Extra filters are not part of the paged-query key: trigger an explicit reload.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobFunctionId, departmentId, employmentStatus])

  const columns: Column<EmployeeListItem>[] = [
    { key: 'number', header: 'Nummer', width: '120px', render: (row) => <code>{row.employeeNumber}</code> },
    { key: 'name', header: 'Naam', render: (row) => `${row.lastName}, ${row.firstName}` },
    {
      key: 'functions',
      header: 'Functies',
      render: (row) =>
        row.functionNames.length > 0 ? (
          <span className="employees-function-badges">
            {row.functionNames.map((name) => (
              <Badge key={name} tone="info">
                {name}
              </Badge>
            ))}
            {row.isDriver && <Badge tone="neutral">Chauffeursprofiel</Badge>}
          </span>
        ) : (
          '—'
        ),
    },
    { key: 'department', header: 'Afdeling', width: '150px', render: (row) => row.departmentName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '220px',
      render: (row) => (
        <StatusBadges
          active={row.isActive}
          operational={{
            label: EMPLOYMENT_STATUS_LABELS[row.employmentStatus],
            tone: EMPLOYMENT_STATUS_TONES[row.employmentStatus],
          }}
        />
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel' }]} />
      <PageHeader
        title="Personeel"
        action={
          hasPermission('employees.create') && (
            <Button onClick={() => navigate('/employees/new')}>Nieuwe medewerker</Button>
          )
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op nummer, naam, e-mail of plaats…"
        activeFilter={activeFilter}
        onActiveFilterChange={(value) => {
          setActiveFilter(value)
          setPage(1)
        }}
      >
        <select
          aria-label="Filter op functie"
          value={jobFunctionId}
          onChange={(e) => {
            setJobFunctionId(e.target.value)
            setPage(1)
          }}
        >
          <option value="">Alle functies</option>
          {jobFunctions.options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </select>
        <select
          aria-label="Filter op afdeling"
          value={departmentId}
          onChange={(e) => {
            setDepartmentId(e.target.value)
            setPage(1)
          }}
        >
          <option value="">Alle afdelingen</option>
          {departments.options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </select>
        <select
          aria-label="Filter op dienstverband"
          value={employmentStatus}
          onChange={(e) => {
            setEmploymentStatus(e.target.value as EmploymentStatus | '')
            setPage(1)
          }}
        >
          <option value="">Alle statussen</option>
          {Object.entries(EMPLOYMENT_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </FilterBar>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Geen medewerkers gevonden."
        loadingMessage="Medewerkers laden…"
        onRowClick={(row) => navigate(`/employees/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
