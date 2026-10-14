import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { bulkChangeOrderStatus, searchTransportOrders } from '../api/transportOrdersApi'
import { formatDate } from '../../../utils/dates'
import {
  ORDER_STATUS_LABELS,
  ORDER_STATUS_TONE,
  ORDER_STATUSES,
  type TransportOrderListItem,
  type TransportOrderStatus,
} from '../types'
import './transport-orders.css'
import { TruncatedText } from '../../../components/ui/TruncatedText'

/** B26: the list state lives in the URL — refresh, Back and a shared link restore the exact view. */
const LIST_STATE = {
  defaults: { search: '', status: '', sort: '', dir: '' },
  allowed: { status: ORDER_STATUSES, sort: ['number', 'date', 'customer', 'status', 'reference'], dir: ['asc', 'desc'] },
}
const DESC_BY_DEFAULT = ['date']

export function TransportOrdersPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const toast = useToast()
  const { hasAnyPermission } = useAuth()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const statusFilter = state.status as TransportOrderStatus | ''
  const [density, setDensity] = useListDensity('transport-orders')
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
      if (state.sort) query.set('sort', state.sort)
      if (state.dir) query.set('dir', state.dir)
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
    (args) => searchTransportOrders({ ...args, status: statusFilter || undefined, sort: state.sort, dir: state.dir as 'asc' | 'desc' | '' }),
    {
      search,
      page,
      errorMessage: t('transportOrders.list.loadFailed'),
      // Filter and sort are part of the request key, so a change refetches (no manual reload).
      extra: { status: statusFilter, sort: state.sort, dir: state.dir },
    },
  )

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
    {
      key: 'number',
      header: t('transportOrders.list.columns.number'),
      width: '118px',
      sortKey: 'number',
      // B14: the identifier is a real link (middle-click, keyboard) that never wraps.
      render: (row) => (
        <Link to={`/transport-orders/${row.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
          {row.orderNumber}
        </Link>
      ),
    },
    { key: 'date', header: t('transportOrders.list.columns.date'), width: '108px', sortKey: 'date', render: (row) => <span className="ui-nowrap">{formatDate(row.orderDate)}</span> },
    { key: 'customer', header: t('transportOrders.list.columns.customer'), sortKey: 'customer', render: (row) => <TruncatedText tooltip={row.customerName}>{row.customerName}</TruncatedText> },
    {
      key: 'reference',
      header: t('transportOrders.list.columns.reference'),
      width: '150px',
      sortKey: 'reference',
      render: (row) => (row.customerReference ? <TruncatedText tooltip={row.customerReference}>{row.customerReference}</TruncatedText> : '—'),
    },
    {
      key: 'route',
      header: t('transportOrders.list.columns.route'),
      render: (row) => {
        // D2: an on-site lifting job has no route — it shows its work site ("Werf: Waver").
        const text = row.craneJobKind === 'OnSiteLifting'
          ? `${t('stopEditor.stopType.Site')}: ${row.firstSiteCity ?? '—'}`
          : row.firstLoadingCity || row.lastUnloadingCity
          ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}${
              row.stopCount > 2 ? ` ${t('transportOrders.list.stopCount', { count: row.stopCount })}` : ''
            }`
          : '—'
        return <TruncatedText tooltip={text}>{text}</TruncatedText>
      },
    },
    {
      key: 'goods',
      header: t('transportOrders.list.columns.goods'),
      render: (row) => (
        <span className="to-goods" title={row.goodsDescription ?? undefined}>
          <TruncatedText tooltip={row.goodsDescription ?? ''}>{row.goodsDescription ?? '—'}</TruncatedText>
          {row.adrRequired && <Badge tone="danger">{t('transportOrders.badges.adr')}</Badge>}
          {row.craneRequired && <Badge tone="info">{t('transportOrders.badges.crane')}</Badge>}
        </span>
      ),
    },
    {
      key: 'status',
      header: t('transportOrders.list.columns.status'),
      width: '130px',
      sortKey: 'status',
      render: (row) => <Badge tone={ORDER_STATUS_TONE[row.status]}>{t(ORDER_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
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
      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('transportOrders.list.searchPlaceholder')}
        onClear={isFiltered ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      >
        <select
          value={statusFilter}
          onChange={(e) => apply({ status: e.target.value })}
          className="ui-filter-select"
          aria-label={t('ui.filter.statusLabel')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {ORDER_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(ORDER_STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </FilterBar>
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
        fixedLayout
        density={density}
        sort={tableSortOf(state.sort, state.dir, DESC_BY_DEFAULT)}
        onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('transportOrders.list.empty')}
        emptyAction={hasAnyPermission(['orders.create', 'orders.manage']) ? <Button onClick={() => navigate('/transport-orders/new')}>{t('transportOrders.list.newOrder')}</Button> : undefined}
        onRetry={reload}
        loadingMessage={t('transportOrders.list.loading')}
        onRowClick={(row) => navigate(`/transport-orders/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />
    </div>
  )
}
