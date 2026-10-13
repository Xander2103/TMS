import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ORDER_STATUS_TONE } from '../../transport-orders/types'
import { getPortalContext, listPortalOrders, type PortalOrderListItem } from '../api/customerPortalApi'
import { orderStatusLabel } from './portalStatusLabels'

/** Customer-portal order overview: only the authenticated user's own customer's orders. */
export function CustomerPortalOrdersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { t, formatDate } = useLocale()
  const canSubmit = hasPermission('customer_portal.submit_orders')

  const [customerName, setCustomerName] = useState<string | null>(null)
  const [orders, setOrders] = useState<PortalOrderListItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    let mounted = true
    Promise.all([getPortalContext(), listPortalOrders()])
      .then(([context, rows]) => {
        if (!mounted) return
        setCustomerName(context.customerName)
        setOrders(rows)
        setLoaded(true)
      })
      .catch((err) => {
        if (!mounted) return
        setError(err instanceof Error ? err.message : t('errors.portalLoad'))
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const columns: Column<PortalOrderListItem>[] = [
    {
      key: 'number',
      header: t('orders.list.columns.order'),
      render: (row) => <Link to={`/klantportaal/orders/${row.id}`}>{row.orderNumber}</Link>,
    },
    { key: 'date', header: t('orders.list.columns.date'), render: (row) => formatDate(row.orderDate) },
    { key: 'ref', header: t('orders.list.columns.yourReference'), render: (row) => row.customerReference ?? '—' },
    {
      key: 'route',
      header: t('orders.list.columns.route'),
      render: (row) =>
        row.firstLoadingCity || row.lastUnloadingCity
          ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}`
          : '—',
    },
    { key: 'goods', header: t('orders.list.columns.goods'), render: (row) => row.goodsDescription ?? '—' },
    {
      key: 'status',
      header: t('orders.list.columns.status'),
      render: (row) => <Badge tone={ORDER_STATUS_TONE[row.status]}>{orderStatusLabel(t, row.status)}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName') }]} />
      <PageHeader
        title={t('orders.list.title')}
        subtitle={customerName ?? undefined}
        action={canSubmit && <Button onClick={() => navigate('/klantportaal/new')}>{t('orders.list.newOrder')}</Button>}
      />
      <DataTable
        columns={columns}
        rows={orders}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('orders.list.empty')}
        loadingMessage={t('orders.list.loading')}
      />
    </div>
  )
}
