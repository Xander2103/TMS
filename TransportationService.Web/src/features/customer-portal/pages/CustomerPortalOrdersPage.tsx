import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE } from '../../transport-orders/types'
import { getPortalContext, listPortalOrders, type PortalOrderListItem } from '../api/customerPortalApi'

/** Customer-portal order overview: only the authenticated user's own customer's orders. */
export function CustomerPortalOrdersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
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
        setError(err instanceof Error ? err.message : 'Het portaal kon niet worden geladen.')
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  const columns: Column<PortalOrderListItem>[] = [
    {
      key: 'number',
      header: 'Opdracht',
      render: (row) => <Link to={`/customer-portal/orders/${row.id}`}>{row.orderNumber}</Link>,
    },
    { key: 'date', header: 'Datum', render: (row) => row.orderDate },
    { key: 'ref', header: 'Uw referentie', render: (row) => row.customerReference ?? '—' },
    {
      key: 'route',
      header: 'Route',
      render: (row) =>
        row.firstLoadingCity || row.lastUnloadingCity
          ? `${row.firstLoadingCity ?? '?'} → ${row.lastUnloadingCity ?? '?'}`
          : '—',
    },
    { key: 'goods', header: 'Goederen', render: (row) => row.goodsDescription ?? '—' },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge tone={ORDER_STATUS_TONE[row.status]}>{ORDER_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal' }]} />
      <PageHeader
        title="Mijn transportopdrachten"
        subtitle={customerName ?? undefined}
        action={canSubmit && <Button onClick={() => navigate('/customer-portal/new')}>Nieuwe opdracht indienen</Button>}
      />
      <DataTable
        columns={columns}
        rows={orders}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen opdrachten. Dien uw eerste transportopdracht in via de knop rechtsboven."
        loadingMessage="Opdrachten laden..."
      />
    </div>
  )
}
