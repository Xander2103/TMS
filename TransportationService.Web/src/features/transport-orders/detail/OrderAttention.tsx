import { Link } from 'react-router-dom'
import { AlertTriangle, Info, OctagonAlert } from 'lucide-react'
import { isPlaceholderCustomerName } from '../components/placeholderCustomer'
import { useOrderDetail } from './orderDetailContext'
import { orderTabPath, type OrderTab } from './orderSections'

interface AttentionItem {
  key: string
  tone: 'warning' | 'danger' | 'info'
  title: string
  text: string
  action?: { label: string; tab: OrderTab }
}

/**
 * Redesign 2026-09-12: the order's attention points as ONE compact strip under the header,
 * each with the subsection that resolves it. Derived from the loaded order — no new endpoint.
 */
export function OrderAttention() {
  const ws = useOrderDetail()
  const { order, pricingStatus, unpricedCoverage, totalPrice } = ws
  const items: AttentionItem[] = []

  if (order.status === 'Cancelled' && order.cancellationReason) {
    items.push({ key: 'cancelled', tone: 'info', title: 'Geannuleerd', text: order.cancellationReason })
  }
  const hasPriceCarrier = Boolean(order.pricingSnapshot) || totalPrice !== null
  if (hasPriceCarrier && order.status !== 'Cancelled') {
    if (order.pricingSnapshot?.isStale) {
      items.push({
        key: 'stale', tone: 'warning', title: 'Prijs verouderd',
        text: 'De goederen of voorwaarden zijn gewijzigd na de laatste berekening. Herbereken de prijs.',
        action: { label: 'Open prijsdetails', tab: 'prijs' },
      })
    }
    if (unpricedCoverage.length > 0 && pricingStatus !== 'Locked' && pricingStatus !== 'Invoiced') {
      items.push({
        key: 'incomplete', tone: 'danger', title: 'Niet alle goederen zijn geprijsd',
        text: `${unpricedCoverage.length} goederenlijn${unpricedCoverage.length === 1 ? '' : 'en'} zonder volledige prijs.`,
        action: { label: 'Open prijsdetails', tab: 'prijs' },
      })
    } else if (pricingStatus === 'Draft' || pricingStatus === 'Reviewed') {
      items.push({
        key: 'toConfirm', tone: 'warning', title: 'Prijs nog te bevestigen',
        text: 'Deze opdracht heeft nog geen bevestigde prijs. Bekijk de prijsdetails of bevestig de prijs om verder te gaan.',
        action: { label: 'Open prijsdetails', tab: 'prijs' },
      })
    }
  }
  if (order.stops.length === 0 && order.status !== 'Cancelled') {
    items.push({
      key: 'noStops', tone: 'warning', title: 'Nog geen stops',
      text: 'De route van deze opdracht is nog niet ingevuld.',
      action: { label: 'Open stops', tab: 'stops' },
    })
  }
  if (isPlaceholderCustomerName(order.customerName)) {
    items.push({
      key: 'placeholder', tone: 'info', title: 'Tijdelijke klant',
      text: 'Koppel de echte klant zodra die bekend is.',
    })
  }

  if (items.length === 0) return null
  const worst = items.some((i) => i.tone === 'danger') ? 'danger' : items.some((i) => i.tone === 'warning') ? 'warning' : 'info'
  return (
    <section className={`tod-attention is-${worst}`} aria-label="Aandachtspunten">
      <ul>
        {items.map((item) => (
          <li key={item.key} className={`tod-attention-item is-${item.tone}`}>
            <span className="tod-attention-icon" aria-hidden>
              {item.tone === 'danger' ? <OctagonAlert size={18} /> : item.tone === 'warning' ? <AlertTriangle size={18} /> : <Info size={18} />}
            </span>
            <span className="tod-attention-text">
              <strong>{item.title}</strong>
              <span>{item.text}</span>
            </span>
            {item.action && (
              <Link to={orderTabPath(order.id, item.action.tab)} className="tod-attention-action">
                {item.action.label}
              </Link>
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}
