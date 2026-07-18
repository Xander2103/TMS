import { useState } from 'react'
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
import { searchDrivers } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverListItem } from '../types'

export function DriversPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<DriverListItem>(
    (args) => searchDrivers(args),
    { search, isActive: activeFilter, page, errorMessage: 'Chauffeurs konden niet worden geladen.' },
  )

  const columns: Column<DriverListItem>[] = [
    { key: 'number', header: 'Nummer', width: '130px', render: (row) => <code>{row.driverNumber}</code> },
    { key: 'name', header: 'Naam', render: (row) => row.fullName },
    { key: 'employee', header: 'Pers.nr', width: '120px', render: (row) => <code>{row.employeeNumber}</code> },
    { key: 'category', header: 'Categorie', render: (row) => row.categoryName ?? '—' },
    {
      key: 'availability',
      header: 'Beschikbaarheid',
      width: '150px',
      render: (row) => <Badge tone={row.availabilityStatus === 'Available' ? 'success' : 'neutral'}>{AVAILABILITY_LABELS[row.availabilityStatus]}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      width: '180px',
      render: (row) => (
        <span style={{ display: 'inline-flex', gap: 6 }}>
          {row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
          {row.isBlocked && <Badge tone="danger">Geblokkeerd</Badge>}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Chauffeurs' }]} />
      <PageHeader
        title="Chauffeurs"
        action={hasPermission('drivers.create') ? <Button onClick={() => navigate('/drivers/new')}>Nieuwe chauffeur</Button> : undefined}
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op nummer, naam of personeelsnummer..."
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
        emptyMessage="Nog geen chauffeurs."
        loadingMessage="Chauffeurs laden..."
        onRowClick={(row) => navigate(`/drivers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
