import { OrderPricingPanel } from '../pricing/OrderPricingPanel'
import { useOrderDetail } from './orderDetailContext'

/**
 * Prijs workspace of the order page. The read model and every handler live in the shell
 * (`useOrderPricingEditor`); the rendering is the shared `OrderPricingPanel`, which the dossier
 * price tab mounts as well (master sprint 2026-09-21, D5).
 */
export function OrderPriceSection() {
  const ws = useOrderDetail()
  return <OrderPricingPanel ws={ws} unitLabel={ws.unitLabel} sectionId="sectie-prijs" />
}
