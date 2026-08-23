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
import { useLocale } from '../../../i18n/localeContext'
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

/** Sort options; labels are i18n keys under employees.list.sort.*. */
const SORT_OPTIONS: EmployeeSortOption[] = [
  'name_asc',
  'name_desc',
  'number',
  'recent',
  'department',
  'function',
  'status',
]

export function EmployeesPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
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
      errorMessage: t('employees.errors.loadEmployees'),
    },
  )

  const columns: Column<EmployeeListItem>[] = [
    { key: 'number', header: t('employees.list.columnNumber'), width: '120px', render: (row) => <code>{row.employeeNumber}</code> },
    { key: 'name', header: t('employees.list.columnName'), render: (row) => `${row.lastName}, ${row.firstName}` },
    {
      key: 'functions',
      header: t('employees.list.columnFunctions'),
      render: (row) =>
        row.functionNames.length > 0 ? (
          <span className="employees-function-badges">
            {row.functionNames.map((name) => (
              <Badge key={name} tone="info">
                {name}
              </Badge>
            ))}
            {row.isDriver && <Badge tone="neutral">{t('employees.list.driverProfileBadge')}</Badge>}
          </span>
        ) : (
          '—'
        ),
    },
    { key: 'department', header: t('employees.list.columnDepartment'), width: '150px', render: (row) => row.departmentName ?? '—' },
    {
      key: 'completeness',
      header: t('employees.list.columnDossier'),
      width: '90px',
      render: (row) => (
        <Badge
          tone={completenessTone(row.completenessPercentage)}
          title={t('employees.completeness.percent', { percentage: row.completenessPercentage })}
        >
          {row.completenessPercentage}%
        </Badge>
      ),
    },
    ...(isDriversView
      ? [
          {
            key: 'availability',
            header: t('employees.list.columnAvailability'),
            width: '150px',
            render: (row: EmployeeListItem) =>
              row.driverAvailability ? (
                <Badge tone={row.driverAvailability === 'Available' ? 'success' : 'neutral'}>
                  {t(DRIVER_AVAILABILITY_LABELS[row.driverAvailability])}
                </Badge>
              ) : (
                '—'
              ),
          },
        ]
      : []),
    {
      key: 'status',
      header: t('employees.list.columnStatus'),
      width: '220px',
      render: (row) => {
        const endBadge = contractEndBadge(row)
        return (
          <>
            <StatusBadges
              active={row.isActive}
              operational={{
                label: t(EMPLOYMENT_STATUS_LABELS[row.employmentStatus]),
                tone: EMPLOYMENT_STATUS_TONES[row.employmentStatus],
              }}
              blocked={isDriversView ? { isBlocked: row.driverIsBlocked ?? false } : undefined}
            />
            {endBadge && <Badge tone={endBadge.tone}>{t(endBadge.key, endBadge.params)}</Badge>}
          </>
        )
      },
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: isDriversView ? t('employees.list.driversTitle') : t('employees.list.title') }]} />
      <PageHeader
        title={isDriversView ? t('employees.list.driversTitle') : t('employees.list.title')}
        subtitle={isDriversView ? t('employees.list.driversSubtitle') : undefined}
        action={
          isDriversView
            ? hasPermission('drivers.create') && (
                <Button onClick={() => navigate('/drivers/new')}>{t('employees.list.newDriver')}</Button>
              )
            : hasPermission('employees.create') && (
                <Button onClick={() => navigate('/employees/new')}>{t('employees.list.newEmployee')}</Button>
              )
        }
      />
      {hasPermission('drivers.view') && (
        <div className="employees-view-toggle" role="tablist" aria-label={t('employees.list.viewToggleLabel')}>
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
            {t('employees.list.allEmployees')}
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
            {t('employees.list.driversTitle')}
          </button>
        </div>
      )}
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder={t('employees.list.searchPlaceholder')}
        activeFilter={filters.activeFilter}
        onActiveFilterChange={(value) => updateFilters({ activeFilter: value })}
      >
        <select
          aria-label={t('employees.list.filterFunction')}
          value={filters.jobFunctionId}
          onChange={(e) => updateFilters({ jobFunctionId: e.target.value })}
        >
          <option value="">{t('employees.list.allFunctions')}</option>
          {jobFunctions.options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </select>
        <select
          aria-label={t('employees.list.filterDepartment')}
          value={filters.departmentId}
          onChange={(e) => updateFilters({ departmentId: e.target.value })}
        >
          <option value="">{t('employees.list.allDepartments')}</option>
          {departments.options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </select>
        <select
          aria-label={t('employees.list.filterEmployment')}
          value={filters.employmentStatus}
          onChange={(e) => updateFilters({ employmentStatus: e.target.value as EmploymentStatus | '' })}
        >
          <option value="">{t('employees.list.allStatuses')}</option>
          {Object.entries(EMPLOYMENT_STATUS_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
        <select
          aria-label={t('employees.list.sortLabel')}
          value={filters.sort}
          onChange={(e) => updateFilters({ sort: e.target.value as EmployeeSortOption })}
        >
          {SORT_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {t(`employees.list.sort.${option}`)}
            </option>
          ))}
        </select>
        <label className="employees-incomplete-filter">
          <input
            type="checkbox"
            checked={filters.incompleteOnly}
            onChange={(e) => updateFilters({ incompleteOnly: e.target.checked })}
          />
          {t('employees.list.incompleteOnly')}
        </label>
      </FilterBar>
      {filters.incompleteOnly && filters.activeFilter === false && (
        <p className="ui-form-field-hint">
          {t('employees.list.incompleteOnlyHint')}
        </p>
      )}
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('employees.list.empty')}
        loadingMessage={t('employees.list.loading')}
        onRowClick={(row) => navigate(`/employees/${row.id}`)}
        rowClassName={(row) => (row.isActive ? undefined : 'employees-row-inactive')}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
