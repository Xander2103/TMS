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
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { searchVehicles } from '../api/vehiclesApi'
import { OPERATIONAL_STATUS_LABELS, OPERATIONAL_STATUS_TONES, type VehicleListItem } from '../types'

const STATUS_TONE: Record<VehicleListItem['operationalStatus'], BadgeTone> = OPERATIONAL_STATUS_TONES

export function VehiclesPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<VehicleListItem>(
    (args) => searchVehicles(args),
    { search, isActive: activeFilter, page, errorMessage: t('vehicles.list.loadFailed') },
  )

  const columns: Column<VehicleListItem>[] = [
    { key: 'number', header: t('fleet.list.colNumber'), width: '120px', render: (row) => <code>{row.internalNumber}</code> },
    { key: 'plate', header: t('fleet.list.colPlate'), width: '130px', render: (row) => <code>{row.licensePlate}</code> },
    { key: 'brand', header: t('fleet.list.colBrandModel'), render: (row) => [row.brand, row.model].filter(Boolean).join(' ') || '—' },
    { key: 'category', header: t('fleet.list.colCategory'), render: (row) => row.categoryName ?? '—' },
    {
      key: 'status',
      header: t('fleet.list.colStatus'),
      width: '150px',
      render: (row) => <Badge tone={STATUS_TONE[row.operationalStatus]}>{t(OPERATIONAL_STATUS_LABELS[row.operationalStatus])}</Badge>,
    },
    {
      key: 'active',
      header: t('fleet.list.colActive'),
      width: '90px',
      render: (row) => (row.isActive ? <Badge tone="success">{t('fleet.common.yes')}</Badge> : <Badge tone="neutral">{t('fleet.common.no')}</Badge>),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.vehicles') }]} />
      <PageHeader
        title={t('navigation.menu.vehicles')}
        action={hasPermission('vehicles.create') ? <Button onClick={() => navigate('/vehicles/new')}>{t('vehicles.list.new')}</Button> : undefined}
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder={t('fleet.list.searchPlaceholder')}
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
        emptyMessage={t('vehicles.list.empty')}
        loadingMessage={t('vehicles.list.loading')}
        onRowClick={(row) => navigate(`/vehicles/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
