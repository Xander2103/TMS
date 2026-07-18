import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { searchLocations } from '../api/locationsApi'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationListItem, type LocationType } from '../types'
import './locations.css'

export function LocationsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [typeFilter, setTypeFilter] = useState<LocationType | ''>('')
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<LocationListItem>(
    (args) => searchLocations({ ...args, type: typeFilter || undefined }),
    { search, isActive: activeFilter, page, errorMessage: 'Locaties konden niet worden geladen.' },
  )

  // The type filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [typeFilter])

  const columns: Column<LocationListItem>[] = [
    { key: 'code', header: 'Code', width: '120px', render: (row) => <code>{row.code}</code> },
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'type', header: 'Type', width: '150px', render: (row) => LOCATION_TYPE_LABELS[row.type] },
    { key: 'city', header: 'Plaats', render: (row) => row.city ?? '—' },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '110px',
      render: (row) => (row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties' }]} />
      <PageHeader
        title="Locaties"
        action={hasPermission('locations.create') ? <Button onClick={() => navigate('/locations/new')}>Nieuwe locatie</Button> : undefined}
      />
      <div className="locations-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder="Zoeken op code, naam of plaats..."
          activeFilter={activeFilter}
          onActiveFilterChange={(value) => {
            setActiveFilter(value)
            setPage(1)
          }}
        />
        <select
          value={typeFilter}
          onChange={(e) => {
            setTypeFilter(e.target.value as LocationType | '')
            setPage(1)
          }}
          className="locations-type-filter"
        >
          <option value="">Alle types</option>
          {LOCATION_TYPES.map((t) => (
            <option key={t} value={t}>
              {LOCATION_TYPE_LABELS[t]}
            </option>
          ))}
        </select>
      </div>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen locaties."
        loadingMessage="Locaties laden..."
        onRowClick={(row) => navigate(`/locations/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
