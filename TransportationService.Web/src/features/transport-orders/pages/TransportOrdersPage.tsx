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
import { searchTransportOrders } from '../api/transportOrdersApi'
import {
  ORDER_STATUS_LABELS,
  ORDER_STATUS_TONE,
  ORDER_STATUSES,
  type TransportOrderListItem,
  type TransportOrderStatus,
} from '../types'
import './transport-orders.css'

export function TransportOrdersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<TransportOrderStatus | ''>('')
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<TransportOrderListItem>(
    (args) => searchTransportOrders({ ...args, status: statusFilter || undefined }),
    { search, page, errorMessage: 'Transportopdrachten konden niet worden geladen.' },
  )

  // The status filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter])

  const columns: Column<TransportOrderListItem>[] = [
    { key: 'number', header: 'Nummer', width: '110px', render: (row) => <code>{row.orderNumber}</code> },
    { key: 'date', header: 'Datum', width: '110px', render: (row) => row.orderDate },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName },
    {
      key: 'route',
      header: 'Route',
      render: (row) =>
        row.firstLoadingCity || row.lastUnloadingCity
          ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}${row.stopCount > 2 ? ` (${row.stopCount} stops)` : ''}`
          : '—',
    },
    {
      key: 'goods',
      header: 'Goederen',
      render: (row) => (
        <span className="to-goods" title={row.goodsDescription}>
          {row.goodsDescription}
          {row.adrRequired && <Badge tone="danger">ADR</Badge>}
          {row.craneRequired && <Badge tone="info">Kraan</Badge>}
        </span>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      width: '130px',
      render: (row) => <Badge tone={ORDER_STATUS_TONE[row.status]}>{ORDER_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Transportopdrachten' }]} />
      <PageHeader
        title="Transportopdrachten"
        action={
          hasPermission('orders.create') ? (
            <Button onClick={() => navigate('/transport-orders/new')}>Nieuwe opdracht</Button>
          ) : undefined
        }
      />
      <div className="to-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder="Zoeken op nummer, klant, referentie of goederen..."
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as TransportOrderStatus | '')
            setPage(1)
          }}
          className="to-status-filter"
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          {ORDER_STATUSES.map((status) => (
            <option key={status} value={status}>
              {ORDER_STATUS_LABELS[status]}
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
        emptyMessage="Nog geen transportopdrachten."
        loadingMessage="Transportopdrachten laden..."
        onRowClick={(row) => navigate(`/transport-orders/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
