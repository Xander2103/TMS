import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { UNIT_TYPE_LABELS } from '../../packages/types'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, STOP_TYPE_LABELS } from '../../transport-orders/types'
import { getPortalOrder, type PortalOrderDetail } from '../api/customerPortalApi'

function formatWindow(from: string | null, to: string | null): string {
  const fmt = (value: string) => value.slice(0, 16).replace('T', ' ')
  if (from && to) return `${fmt(from)} – ${fmt(to)}`
  if (from) return `vanaf ${fmt(from)}`
  if (to) return `tot ${fmt(to)}`
  return '—'
}

/** Customer-facing order detail: status, stops and cargo — no internal pricing or planning. */
export function CustomerPortalOrderDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const [order, setOrder] = useState<PortalOrderDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPortalOrder(id)
      .then((data) => {
        if (mounted) setOrder(data)
      })
      .catch(() => {
        if (mounted) setError('De opdracht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  if (error) return <ErrorState message={error} />
  if (!order) return <LoadingState message="Opdracht laden..." />

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/customer-portal' }, { label: order.orderNumber }]} />
      <BackButton to="/customer-portal" label="Terug naar mijn opdrachten" />
      <PageHeader
        title={order.orderNumber}
        subtitle={`Ingediend voor ${order.orderDate}${order.customerReference ? ` · uw ref. ${order.customerReference}` : ''}`}
        action={<Badge tone={ORDER_STATUS_TONE[order.status]}>{ORDER_STATUS_LABELS[order.status]}</Badge>}
      />

      {order.cancellationReason && (
        <p className="to-cancel-reason" role="note">
          Geannuleerd: {order.cancellationReason}
        </p>
      )}

      <section className="to-section">
        <h2>Stops</h2>
        <table className="to-stops-table">
          <thead>
            <tr>
              <th>#</th>
              <th>Type</th>
              <th>Locatie</th>
              <th>Gevraagd venster</th>
              <th>Referentie</th>
            </tr>
          </thead>
          <tbody>
            {order.stops.map((stop) => (
              <tr key={stop.sequence}>
                <td>{stop.sequence}</td>
                <td>{STOP_TYPE_LABELS[stop.stopType]}</td>
                <td>
                  {stop.locationName}
                  {stop.city ? `, ${stop.city}` : ''}
                </td>
                <td>{formatWindow(stop.requestedFrom, stop.requestedTo)}</td>
                <td>{stop.reference ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      {order.cargoItems.length > 0 && (
        <section className="to-section">
          <h2>Goederen</h2>
          <table className="to-stops-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Omschrijving</th>
                <th>Aantal</th>
                <th>Type</th>
                <th>ADR</th>
              </tr>
            </thead>
            <tbody>
              {order.cargoItems.map((item) => (
                <tr key={item.sequence}>
                  <td>{item.sequence}</td>
                  <td>{item.description}</td>
                  <td>
                    {item.expectedQuantity} {item.quantityUnit ?? ''}
                  </td>
                  <td>{item.unitType ? UNIT_TYPE_LABELS[item.unitType] : '—'}</td>
                  <td>{item.adrRequired ? <Badge tone="danger">ADR</Badge> : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {order.notes && (
        <section className="to-section">
          <h2>Uw opmerkingen</h2>
          <p>{order.notes}</p>
        </section>
      )}
    </div>
  )
}
