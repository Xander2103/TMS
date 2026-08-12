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
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { searchLocations, searchLocationsGrouped } from '../api/locationsApi'
import {
  LOCATION_TYPE_LABELS,
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

const STATUS_BADGE = (row: LocationListItem) =>
  row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>

/**
 * Locations overview: one shared filter bar (zoeken, status, type, klant, land, postcode)
 * above either the flat sortable table or the per-customer grouped view. The choice
 * persists in localStorage; row click opens the detail (all actions live there).
 */
export function LocationsPage() {
  const navigate = useNavigate()
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
      <Breadcrumbs items={[{ label: 'Locaties' }]} />
      <PageHeader
        title="Locaties"
        action={
          hasPermission('locations.create') ? (
            <Button onClick={() => navigate('/locations/new')}>Nieuwe locatie</Button>
          ) : undefined
        }
      />
      <div className="locations-filters">
        <FilterBar
          search={search}
          onSearchChange={setSearch}
          searchPlaceholder="Zoeken op code, naam of plaats..."
          activeFilter={activeFilter}
          onActiveFilterChange={setActiveFilter}
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as LocationType | '')}
          className="locations-type-filter"
          aria-label="Type"
        >
          <option value="">Alle types</option>
          {LOCATION_TYPES.map((t) => (
            <option key={t} value={t}>
              {LOCATION_TYPE_LABELS[t]}
            </option>
          ))}
        </select>
        <div className="locations-customer-filter">
          <SearchableSelect
            ariaLabel="Klant"
            placeholder="Alle klanten"
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
            placeholder="Land"
          />
        </div>
        <input
          className="locations-postal-filter"
          value={postalCodeFilter}
          onChange={(e) => setPostalCodeFilter(e.target.value)}
          placeholder="Postcode"
          aria-label="Postcode"
        />
        <div className="locations-view-toggle" role="group" aria-label="Weergave">
          <span className="locations-view-toggle-label">Weergave:</span>
          <button
            type="button"
            className="locations-view-toggle-button"
            aria-pressed={viewMode === 'flat'}
            onClick={() => switchView('flat')}
          >
            Plat
          </button>
          <button
            type="button"
            className="locations-view-toggle-button"
            aria-pressed={viewMode === 'grouped'}
            onClick={() => switchView('grouped')}
          >
            Per klant
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
      errorMessage: 'Locaties konden niet worden geladen.',
      extra: { type: filters.type, customerId: filters.customerId, country: filters.country, postalCode: filters.postalCode, sort },
    },
  )

  const columns: Column<LocationListItem>[] = [
    { key: 'code', header: 'Code', width: '130px', sortKey: 'code', render: (row) => <code>{row.code}</code> },
    { key: 'name', header: 'Naam', sortKey: 'name', render: (row) => row.name },
    { key: 'type', header: 'Type', width: '160px', sortKey: 'type', render: (row) => LOCATION_TYPE_LABELS[row.type] },
    { key: 'city', header: 'Plaats', sortKey: 'city', render: (row) => row.city ?? '—' },
    { key: 'customer', header: 'Klant', sortKey: 'customer', render: (row) => row.customerName ?? '—' },
    { key: 'status', header: 'Status', width: '110px', sortKey: 'status', render: STATUS_BADGE },
  ]

  return (
    <>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Geen locaties gevonden."
        loadingMessage="Locaties laden..."
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
      errorMessage: 'Locaties konden niet worden geladen.',
      extra: { type: filters.type, customerId: filters.customerId, country: filters.country, postalCode: filters.postalCode, innerSort },
    },
  )

  const groupColumns: Column<LocationListItem>[] = useMemo(
    () => [
      { key: 'code', header: 'Code', width: '130px', render: (row) => <code>{row.code}</code> },
      { key: 'name', header: 'Naam', render: (row) => row.name },
      { key: 'type', header: 'Type', width: '160px', render: (row) => LOCATION_TYPE_LABELS[row.type] },
      { key: 'city', header: 'Plaats', render: (row) => row.city ?? '—' },
      { key: 'status', header: 'Status', width: '110px', render: STATUS_BADGE },
    ],
    [],
  )

  if (isLoading) return <p className="placeholder-text">Locaties laden...</p>
  if (error) return <p className="placeholder-text">{error}</p>

  return (
    <div className="locations-grouped">
      <div className="locations-grouped-toolbar">
        <label htmlFor="locations-inner-sort">Sorteer binnen klant</label>
        <select
          id="locations-inner-sort"
          value={innerSort}
          onChange={(e) => setInnerSort(e.target.value as InnerSort)}
        >
          <option value="name">Naam</option>
          <option value="code">Code</option>
          <option value="city">Plaats</option>
        </select>
      </div>

      {items.length === 0 && <p className="placeholder-text">Geen locaties gevonden.</p>}

      {items.map((group) => {
        const key = group.customerId ?? 'unlinked'
        const title = group.customerName ?? 'Ongekoppelde locaties'
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
                emptyMessage="Geen locaties in deze groep."
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
