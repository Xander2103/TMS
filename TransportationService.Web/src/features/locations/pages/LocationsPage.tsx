import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column, type SortState } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useLocale } from '../../../i18n/localeContext'
import type { TranslateFn } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { searchLocations, searchLocationsGrouped } from '../api/locationsApi'
import {
  LOCATION_TYPE_LABEL_KEYS,
  LOCATION_TYPES,
  type LocationGroup,
  type LocationListItem,
  type LocationType,
} from '../types'
import './locations.css'

type ViewMode = 'flat' | 'grouped'
type InnerSort = 'name' | 'code' | 'city'

const VIEW_MODE_STORAGE_KEY = 'locations.viewMode'

function readStoredViewMode(): ViewMode {
  try {
    return window.localStorage.getItem(VIEW_MODE_STORAGE_KEY) === 'grouped' ? 'grouped' : 'flat'
  } catch {
    return 'flat'
  }
}

/** Filters shared by both views; the view components fold them into their request key. */
interface LocationFilters {
  search: string
  isActive: boolean | undefined
  type: LocationType | ''
  customerId: string | null
  country: string | null
  postalCode: string
}

const statusBadge = (t: TranslateFn) => (row: LocationListItem) =>
  row.isActive ? (
    <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
  ) : (
    <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
  )

/**
 * Locations overview: one shared filter bar (zoeken, status, type, klant, land, postcode)
 * above either the flat sortable table or the per-customer grouped view. The choice
 * persists in localStorage; row click opens the detail (all actions live there).
 */
export function LocationsPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()

  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [typeFilter, setTypeFilter] = useState<LocationType | ''>('')
  const [customerFilter, setCustomerFilter] = useState<string | null>(null)
  const [countryFilter, setCountryFilter] = useState<string | null>(null)
  const [postalCodeFilter, setPostalCodeFilter] = useState('')
  const [viewMode, setViewMode] = useState<ViewMode>(readStoredViewMode)

  const [customerOptions, setCustomerOptions] = useState<{ value: string; label: string }[]>([])
  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name })))
      })
      .catch(() => {
        /* filter stays usable without options */
      })
    return () => {
      mounted = false
    }
  }, [])

  function switchView(mode: ViewMode) {
    setViewMode(mode)
    try {
      window.localStorage.setItem(VIEW_MODE_STORAGE_KEY, mode)
    } catch {
      /* persistence is best-effort */
    }
  }

  const filters: LocationFilters = {
    search,
    isActive: activeFilter,
    type: typeFilter,
    customerId: customerFilter,
    country: countryFilter,
    postalCode: postalCodeFilter,
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.locations') }]} />
      <PageHeader
        title={t('navigation.menu.locations')}
        action={
          hasPermission('locations.create') ? (
            <Button onClick={() => navigate('/locations/new')}>{t('locations.list.new')}</Button>
          ) : undefined
        }
      />
      <div className="locations-filters">
        <FilterBar
          search={search}
          onSearchChange={setSearch}
          searchPlaceholder={t('locations.list.searchPlaceholder')}
          activeFilter={activeFilter}
          onActiveFilterChange={setActiveFilter}
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as LocationType | '')}
          className="locations-type-filter"
          aria-label={t('locations.list.typeLabel')}
        >
          <option value="">{t('locations.list.allTypes')}</option>
          {LOCATION_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(LOCATION_TYPE_LABEL_KEYS[type])}
            </option>
          ))}
        </select>
        <div className="locations-customer-filter">
          <SearchableSelect
            ariaLabel={t('locations.list.customerLabel')}
            placeholder={t('locations.list.allCustomers')}
            value={customerFilter}
            onChange={setCustomerFilter}
            options={customerOptions}
          />
        </div>
        <div className="locations-country-filter">
          <CountryCombobox
            id="locations-country-filter"
            value={countryFilter}
            onChange={setCountryFilter}
            placeholder={t('locations.list.countryPlaceholder')}
          />
        </div>
        <input
          className="locations-postal-filter"
          value={postalCodeFilter}
          onChange={(e) => setPostalCodeFilter(e.target.value)}
          placeholder={t('locations.list.postalCodeLabel')}
          aria-label={t('locations.list.postalCodeLabel')}
        />
        <div className="locations-view-toggle" role="group" aria-label={t('locations.list.viewLabel')}>
          <span className="locations-view-toggle-label">{t('locations.list.viewLabelText')}</span>
          <button
            type="button"
            className="locations-view-toggle-button"
            aria-pressed={viewMode === 'flat'}
            onClick={() => switchView('flat')}
          >
            {t('locations.list.viewFlat')}
          </button>
          <button
            type="button"
            className="locations-view-toggle-button"
            aria-pressed={viewMode === 'grouped'}
            onClick={() => switchView('grouped')}
          >
            {t('locations.list.viewGrouped')}
          </button>
        </div>
      </div>

      {viewMode === 'flat' ? <FlatLocationsView filters={filters} /> : <GroupedLocationsView filters={filters} />}
    </div>
  )
}

/** Flat table with server-side sorting on Code/Naam/Type/Plaats/Klant/Status. */
function FlatLocationsView({ filters }: { filters: LocationFilters }) {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<SortState | null>(null)

  // Any filter change restarts at page 1 — otherwise a shrunken result set can leave the
  // user stranded on a page past the end. Adjusted during render (React's endorsed pattern)
  // instead of in an effect.
  const filterKey = JSON.stringify(filters)
  const [prevFilterKey, setPrevFilterKey] = useState(filterKey)
  if (prevFilterKey !== filterKey) {
    setPrevFilterKey(filterKey)
    setPage(1)
  }

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<LocationListItem>(
    (args) =>
      searchLocations({
        ...args,
        type: filters.type || undefined,
        customerId: filters.customerId ?? undefined,
        country: filters.country ?? undefined,
        postalCode: filters.postalCode || undefined,
        sort: sort?.key,
        dir: sort?.dir,
      }),
    {
      search: filters.search,
      isActive: filters.isActive,
      page,
      errorMessage: t('locations.list.loadFailed'),
      extra: { type: filters.type, customerId: filters.customerId, country: filters.country, postalCode: filters.postalCode, sort },
    },
  )

  const columns: Column<LocationListItem>[] = [
    { key: 'code', header: t('locations.list.columns.code'), width: '130px', sortKey: 'code', render: (row) => <code>{row.code}</code> },
    { key: 'name', header: t('locations.list.columns.name'), sortKey: 'name', render: (row) => row.name },
    { key: 'type', header: t('locations.list.columns.type'), width: '160px', sortKey: 'type', render: (row) => t(LOCATION_TYPE_LABEL_KEYS[row.type]) },
    { key: 'city', header: t('locations.list.columns.city'), sortKey: 'city', render: (row) => row.city ?? '—' },
    { key: 'customer', header: t('locations.list.columns.customer'), sortKey: 'customer', render: (row) => row.customerName ?? '—' },
    { key: 'status', header: t('locations.list.columns.status'), width: '110px', sortKey: 'status', render: statusBadge(t) },
  ]

  return (
    <>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('locations.list.empty')}
        loadingMessage={t('locations.list.loading')}
        onRowClick={(row) => navigate(`/locations/${row.id}`)}
        rowClassName={(row) => (row.isActive ? undefined : 'locations-row-inactive')}
        sort={sort}
        onSortChange={setSort}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </>
  )
}

/** Per-customer view over GET /api/locations/grouped: collapsible groups, paged per GROUP. */
function GroupedLocationsView({ filters }: { filters: LocationFilters }) {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [page, setPage] = useState(1)
  const [innerSort, setInnerSort] = useState<InnerSort>('name')
  // Groups the user explicitly collapsed; everything else renders open.
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})

  const filterKey = JSON.stringify(filters)
  const [prevFilterKey, setPrevFilterKey] = useState(filterKey)
  if (prevFilterKey !== filterKey) {
    setPrevFilterKey(filterKey)
    setPage(1)
  }

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<LocationGroup>(
    (args) =>
      searchLocationsGrouped({
        search: args.search || undefined,
        isActive: args.isActive,
        type: filters.type || undefined,
        customerId: filters.customerId ?? undefined,
        country: filters.country ?? undefined,
        postalCode: filters.postalCode || undefined,
        innerSort,
        page: args.page,
        pageSize: args.pageSize,
      }),
    {
      search: filters.search,
      isActive: filters.isActive,
      page,
      errorMessage: t('locations.list.loadFailed'),
      extra: { type: filters.type, customerId: filters.customerId, country: filters.country, postalCode: filters.postalCode, innerSort },
    },
  )

  const groupColumns: Column<LocationListItem>[] = useMemo(
    () => [
      { key: 'code', header: t('locations.list.columns.code'), width: '130px', render: (row) => <code>{row.code}</code> },
      { key: 'name', header: t('locations.list.columns.name'), render: (row) => row.name },
      { key: 'type', header: t('locations.list.columns.type'), width: '160px', render: (row) => t(LOCATION_TYPE_LABEL_KEYS[row.type]) },
      { key: 'city', header: t('locations.list.columns.city'), render: (row) => row.city ?? '—' },
      { key: 'status', header: t('locations.list.columns.status'), width: '110px', render: statusBadge(t) },
    ],
    [t],
  )

  if (isLoading) return <p className="placeholder-text">{t('locations.list.loading')}</p>
  if (error) return <p className="placeholder-text">{error}</p>

  return (
    <div className="locations-grouped">
      <div className="locations-grouped-toolbar">
        <label htmlFor="locations-inner-sort">{t('locations.list.innerSortLabel')}</label>
        <select
          id="locations-inner-sort"
          value={innerSort}
          onChange={(e) => setInnerSort(e.target.value as InnerSort)}
        >
          <option value="name">{t('locations.list.innerSortName')}</option>
          <option value="code">{t('locations.list.innerSortCode')}</option>
          <option value="city">{t('locations.list.innerSortCity')}</option>
        </select>
      </div>

      {items.length === 0 && <p className="placeholder-text">{t('locations.list.empty')}</p>}

      {items.map((group) => {
        const key = group.customerId ?? 'unlinked'
        const title = group.customerName ?? t('locations.list.unlinkedGroup')
        const open = !collapsed[key]
        return (
          <section key={key} className="locations-group">
            <h3 className="locations-group-heading">
              <button
                type="button"
                className="locations-group-toggle"
                aria-expanded={open}
                onClick={() => setCollapsed((current) => ({ ...current, [key]: open }))}
              >
                <span className="locations-group-chevron" aria-hidden="true">
                  {open ? '▾' : '▸'}
                </span>
                <span className="locations-group-title">{title}</span>
                <span className="locations-group-count">({group.locations.length})</span>
              </button>
            </h3>
            {open && (
              <DataTable
                columns={groupColumns}
                rows={group.locations}
                rowKey={(row) => row.id}
                emptyMessage={t('locations.list.groupEmpty')}
                onRowClick={(row) => navigate(`/locations/${row.id}`)}
                rowClassName={(row) => (row.isActive ? undefined : 'locations-row-inactive')}
              />
            )}
          </section>
        )
      })}

      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
