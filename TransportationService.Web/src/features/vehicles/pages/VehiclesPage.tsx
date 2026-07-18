import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { searchVehicles } from '../api/vehiclesApi'
import { OPERATIONAL_STATUS_LABELS, type VehicleListItem } from '../types'

const STATUS_TONE: Record<VehicleListItem['operationalStatus'], BadgeTone> = {
  Active: 'success',
  InMaintenance: 'warning',
  OutOfService: 'danger',
  Decommissioned: 'neutral',
}

export function VehiclesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<VehicleListItem>(
    (args) => searchVehicles(args),
    { search, isActive: activeFilter, page, errorMessage: 'Voertuigen konden niet worden geladen.' },
  )

  const columns: Column<VehicleListItem>[] = [
    { key: 'number', header: 'Nummer', width: '120px', render: (row) => <code>{row.internalNumber}</code> },
    { key: 'plate', header: 'Kenteken', width: '130px', render: (row) => <code>{row.licensePlate}</code> },
    { key: 'brand', header: 'Merk / model', render: (row) => [row.brand, row.model].filter(Boolean).join(' ') || '—' },
    { key: 'category', header: 'Categorie', render: (row) => row.categoryName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '150px',
      render: (row) => <Badge tone={STATUS_TONE[row.operationalStatus]}>{OPERATIONAL_STATUS_LABELS[row.operationalStatus]}</Badge>,
    },
    {
      key: 'active',
      header: 'Actief',
      width: '90px',
      render: (row) => (row.isActive ? <Badge tone="success">Ja</Badge> : <Badge tone="neutral">Nee</Badge>),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Voertuigen' }]} />
      <PageHeader
        title="Voertuigen"
        action={hasPermission('vehicles.create') ? <Button onClick={() => navigate('/vehicles/new')}>Nieuw voertuig</Button> : undefined}
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op nummer, kenteken, merk of model..."
        activeFilter={activeFilter}
        onActiveFilterChange={(value) => {
          setActiveFilter(value)
          setPage(1)
        }}
      />
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen voertuigen."
        loadingMessage="Voertuigen laden..."
        onRowClick={(row) => navigate(`/vehicles/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
