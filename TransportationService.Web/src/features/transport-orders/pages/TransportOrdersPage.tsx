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
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { bulkChangeOrderStatus, searchTransportOrders } from '../api/transportOrdersApi'
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
  const { t } = useLocale()
  const toast = useToast()
  const { hasAnyPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<TransportOrderStatus | ''>('')
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [bulkStatus, setBulkStatus] = useState<TransportOrderStatus | ''>('')
  const [bulkBusy, setBulkBusy] = useState(false)
  const canBulk = hasAnyPermission(['orders.change_status', 'orders.manage'])

  function toggleSelected(id: string) {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  async function applyBulkStatus() {
    if (!bulkStatus || selected.size === 0) return
    setBulkBusy(true)
    try {
      const result = await bulkChangeOrderStatus([...selected], bulkStatus)
      if (result.failedCount === 0) {
        toast.showSuccess(t('transportOrders.list.bulkUpdated', { count: result.succeededCount }))
      } else {
        toast.showError(
          t('transportOrders.list.bulkPartial', {
            succeeded: result.succeededCount,
            failed: result.failedCount,
            error: result.results.find((r) => !r.success)?.error ?? t('transportOrders.list.bulkPartialFallback'),
          }),
        )
      }
      setSelected(new Set())
      setBulkStatus('')
      reload()
    } catch {
      toast.showError(t('transportOrders.list.bulkFailed'))
    } finally {
      setBulkBusy(false)
    }
  }

  async function handleExport() {
    setExporting(true)
    try {
      const query = new URLSearchParams()
      if (search) query.set('search', search)
      if (statusFilter) query.set('status', statusFilter)
      const response = await fetch(`${apiBaseUrl}/api/transport-orders/export?${query.toString()}`, {
        headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
      })
      if (!response.ok) throw new Error()
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = 'transportopdrachten.csv'
      anchor.click()
      URL.revokeObjectURL(url)
    } catch {
      toast.showError(t('transportOrders.list.exportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<TransportOrderListItem>(
    (args) => searchTransportOrders({ ...args, status: statusFilter || undefined }),
    { search, page, errorMessage: t('transportOrders.list.loadFailed') },
  )

  // The status filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter])

  const columns: Column<TransportOrderListItem>[] = [
    ...(canBulk
      ? [
          {
            key: 'select',
            header: '',
            width: '36px',
            render: (row: TransportOrderListItem) => (
              <input
                type="checkbox"
                checked={selected.has(row.id)}
                onChange={() => toggleSelected(row.id)}
                onClick={(event) => event.stopPropagation()}
                aria-label={t('transportOrders.list.selectRow', { orderNumber: row.orderNumber })}
              />
            ),
          } satisfies Column<TransportOrderListItem>,
        ]
      : []),
    { key: 'number', header: t('transportOrders.list.columns.number'), width: '110px', render: (row) => <code>{row.orderNumber}</code> },
    { key: 'date', header: t('transportOrders.list.columns.date'), width: '110px', render: (row) => row.orderDate },
    { key: 'customer', header: t('transportOrders.list.columns.customer'), render: (row) => row.customerName },
    {
      key: 'route',
      header: t('transportOrders.list.columns.route'),
      render: (row) =>
        row.firstLoadingCity || row.lastUnloadingCity
          ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}${
              row.stopCount > 2 ? ` ${t('transportOrders.list.stopCount', { count: row.stopCount })}` : ''
            }`
          : '—',
    },
    {
      key: 'goods',
      header: t('transportOrders.list.columns.goods'),
      render: (row) => (
        <span className="to-goods" title={row.goodsDescription ?? undefined}>
          {row.goodsDescription ?? '—'}
          {row.adrRequired && <Badge tone="danger">{t('transportOrders.badges.adr')}</Badge>}
          {row.craneRequired && <Badge tone="info">{t('transportOrders.badges.crane')}</Badge>}
        </span>
      ),
    },
    {
      key: 'status',
      header: t('transportOrders.list.columns.status'),
      width: '130px',
      render: (row) => <Badge tone={ORDER_STATUS_TONE[row.status]}>{t(ORDER_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('transportOrders.list.title') }]} />
      <PageHeader
        title={t('transportOrders.list.title')}
        action={
          <span className="to-header-actions">
            {hasAnyPermission(['orders.export', 'orders.manage']) && (
              <Button variant="secondary" onClick={() => void handleExport()} disabled={exporting}>
                {exporting ? t('transportOrders.list.exporting') : t('transportOrders.list.exportCsv')}
              </Button>
            )}
            {hasAnyPermission(['orders.create', 'orders.manage']) && (
              <Button onClick={() => navigate('/transport-orders/new')}>{t('transportOrders.list.newOrder')}</Button>
            )}
          </span>
        }
      />
      <div className="to-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder={t('transportOrders.list.searchPlaceholder')}
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as TransportOrderStatus | '')
            setPage(1)
          }}
          className="to-status-filter"
          aria-label={t('ui.filter.statusLabel')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {ORDER_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(ORDER_STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </div>
      {canBulk && selected.size > 0 && (
        <div className="to-bulkbar">
          <span>{t('transportOrders.list.selectedCount', { count: selected.size })}</span>
          <select
            value={bulkStatus}
            onChange={(event) => setBulkStatus(event.target.value as TransportOrderStatus | '')}
            aria-label={t('transportOrders.list.newStatusAria')}
          >
            <option value="">{t('transportOrders.list.chooseStatus')}</option>
            {ORDER_STATUSES.map((status) => (
              <option key={status} value={status}>
                {t(ORDER_STATUS_LABELS[status])}
              </option>
            ))}
          </select>
          <Button onClick={() => void applyBulkStatus()} disabled={bulkBusy || !bulkStatus}>
            {t('transportOrders.list.applyStatus')}
          </Button>
          <Button variant="secondary" onClick={() => setSelected(new Set())} disabled={bulkBusy}>
            {t('transportOrders.list.clearSelection')}
          </Button>
        </div>
      )}
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('transportOrders.list.empty')}
        loadingMessage={t('transportOrders.list.loading')}
        onRowClick={(row) => navigate(`/transport-orders/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
