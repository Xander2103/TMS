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
  type EmployeeSortOption,
  type EmploymentStatus,
} from '../types/employee'
import { completenessTone, contractEndBadge } from '../utils/employeeListBadges'
import './employees-page.css'

const FILTER_STORAGE_KEY = 'ts.employees.filters'

interface EmployeesFilters {
  activeFilter: boolean | undefined
  jobFunctionId: string
  departmentId: string
  employmentStatus: EmploymentStatus | ''
  sort: EmployeeSortOption
  incompleteOnly: boolean
}

const EMPTY_FILTERS: EmployeesFilters = {
  activeFilter: undefined,
  jobFunctionId: '',
  departmentId: '',
  employmentStatus: '',
  sort: 'name_asc',
  incompleteOnly: false,
}

function loadStoredFilters(): EmployeesFilters {
  try {
    const raw = localStorage.getItem(FILTER_STORAGE_KEY)
    return raw ? { ...EMPTY_FILTERS, ...(JSON.parse(raw) as Partial<EmployeesFilters>) } : EMPTY_FILTERS
  } catch {
    return EMPTY_FILTERS
  }
}

const SORT_OPTIONS: { value: EmployeeSortOption; label: string }[] = [
  { value: 'name_asc', label: 'Naam A–Z' },
  { value: 'name_desc', label: 'Naam Z–A' },
  { value: 'number', label: 'Personeelsnummer' },
  { value: 'recent', label: 'Recent toegevoegd' },
  { value: 'department', label: 'Afdeling' },
  { value: 'function', label: 'Functie' },
  { value: 'status', label: 'Actief/Inactief' },
]

export function EmployeesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const isDriversView = searchParams.get('view') === 'chauffeurs'
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [filters, setFilters] = useState<EmployeesFilters>(loadStoredFilters)

  const jobFunctions = useLookupOptions('/api/job-functions')
  const departments = useLookupOptions('/api/departments')

  useEffect(() => {
    localStorage.setItem(FILTER_STORAGE_KEY, JSON.stringify(filters))
  }, [filters])

  function updateFilters(patch: Partial<EmployeesFilters>) {
    setFilters((current) => ({ ...current, ...patch }))
    setPage(1)
  }

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<EmployeeListItem>(
    (args) =>
      searchEmployees({
        ...args,
        jobFunctionId: filters.jobFunctionId || undefined,
        departmentId: filters.departmentId || undefined,
        employmentStatus: filters.employmentStatus || undefined,
        hasDriverProfile: isDriversView || undefined,
        sort: filters.sort,
        incompleteOnly: filters.incompleteOnly || undefined,
      }),
    {
      search,
      isActive: filters.activeFilter,
      page,
      // Every filter value that the fetcher closure reads must be folded in here — otherwise
      // usePagedQuery's request key doesn't change and a filter edit silently reuses stale data.
      extra: { ...filters, isDriversView },
      errorMessage: 'Medewerkers konden niet worden geladen.',
    },
  )

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
      key: 'completeness',
      header: 'Dossier',
      width: '90px',
      render: (row) => (
        <Badge tone={completenessTone(row.completenessPercentage)} title={`Dossier ${row.completenessPercentage}% compleet`}>
          {row.completenessPercentage}%
        </Badge>
      ),
    },
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
      render: (row) => {
        const endBadge = contractEndBadge(row)
        return (
          <>
            <StatusBadges
              active={row.isActive}
              operational={{
                label: EMPLOYMENT_STATUS_LABELS[row.employmentStatus],
                tone: EMPLOYMENT_STATUS_TONES[row.employmentStatus],
              }}
              blocked={isDriversView ? { isBlocked: row.driverIsBlocked ?? false } : undefined}
            />
            {endBadge && <Badge tone={endBadge.tone}>{endBadge.label}</Badge>}
          </>
        )
      },
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
        activeFilter={filters.activeFilter}
        onActiveFilterChange={(value) => updateFilters({ activeFilter: value })}
      >
        <select
          aria-label="Filter op functie"
          value={filters.jobFunctionId}
          onChange={(e) => updateFilters({ jobFunctionId: e.target.value })}
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
          value={filters.departmentId}
          onChange={(e) => updateFilters({ departmentId: e.target.value })}
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
          value={filters.employmentStatus}
          onChange={(e) => updateFilters({ employmentStatus: e.target.value as EmploymentStatus | '' })}
        >
          <option value="">Alle statussen</option>
          {Object.entries(EMPLOYMENT_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select
          aria-label="Sorteren"
          value={filters.sort}
          onChange={(e) => updateFilters({ sort: e.target.value as EmployeeSortOption })}
        >
          {SORT_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <label className="employees-incomplete-filter">
          <input
            type="checkbox"
            checked={filters.incompleteOnly}
            onChange={(e) => updateFilters({ incompleteOnly: e.target.checked })}
          />
          Enkel onvolledige dossiers
        </label>
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
        rowClassName={(row) => (row.isActive ? undefined : 'employees-row-inactive')}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
