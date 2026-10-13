import { OrderTimelinePanel } from '../components/OrderTimelinePanel'
import { useOrderDetail } from './orderDetailContext'

/** Historiek workspace: the existing order timeline in one panel. */
export function OrderHistorySection() {
  const { order } = useOrderDetail()
  return (
    <div className="tod-section" id="sectie-historiek">
      <section className="tod-panel">
        <h2>Historiek</h2>
        <OrderTimelinePanel orderId={order.id} />
      </section>
    </div>
  )
}
