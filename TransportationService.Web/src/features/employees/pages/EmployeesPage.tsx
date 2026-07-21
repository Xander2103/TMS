import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
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
  DRIVER_AVAILABILITY_LABELS,
  EMPLOYMENT_STATUS_LABELS,
  EMPLOYMENT_STATUS_TONES,
  type EmployeeListItem,
  type EmploymentStatus,
} from '../types/employee'
import './employees-page.css'

export function EmployeesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const isDriversView = searchParams.get('view') === 'chauffeurs'
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
        hasDriverProfile: isDriversView || undefined,
      }),
    { search, isActive: activeFilter, page, errorMessage: 'Medewerkers konden niet worden geladen.' },
  )

  // Extra filters are not part of the paged-query key: trigger an explicit reload.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobFunctionId, departmentId, employmentStatus, isDriversView])

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
    ...(isDriversView
      ? [
          {
            key: 'availability',
            header: 'Beschikbaarheid',
            width: '150px',
            render: (row: EmployeeListItem) =>
              row.driverAvailability ? (
                <Badge tone={row.driverAvailability === 'Available' ? 'success' : 'neutral'}>
                  {DRIVER_AVAILABILITY_LABELS[row.driverAvailability]}
                </Badge>
              ) : (
                '—'
              ),
          },
        ]
      : []),
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
          blocked={isDriversView ? { isBlocked: row.driverIsBlocked ?? false } : undefined}
        />
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: isDriversView ? 'Chauffeurs' : 'Personeel' }]} />
      <PageHeader
        title={isDriversView ? 'Chauffeurs' : 'Personeel'}
        subtitle={isDriversView ? 'Medewerkers met een chauffeursprofiel.' : undefined}
        action={
          isDriversView
            ? hasPermission('drivers.create') && (
                <Button onClick={() => navigate('/drivers/new')}>Nieuwe chauffeur</Button>
              )
            : hasPermission('employees.create') && (
                <Button onClick={() => navigate('/employees/new')}>Nieuwe medewerker</Button>
              )
        }
      />
      {hasPermission('drivers.view') && (
        <div className="employees-view-toggle" role="tablist" aria-label="Weergave">
          <button
            type="button"
            role="tab"
            aria-selected={!isDriversView}
            className={!isDriversView ? 'active' : ''}
            onClick={() => {
              setSearchParams({}, { replace: true })
              setPage(1)
            }}
          >
            Alle medewerkers
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={isDriversView}
            className={isDriversView ? 'active' : ''}
            onClick={() => {
              setSearchParams({ view: 'chauffeurs' }, { replace: true })
              setPage(1)
            }}
          >
            Chauffeurs
          </button>
        </div>
      )}
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
