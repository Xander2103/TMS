import { CustomerPackagesSummary } from '../../packages/components/CustomerPackagesSummary'
import { OrderPackagesPanel } from '../../packages/components/OrderPackagesPanel'
import { useOrderDetail } from './orderDetailContext'

/** Colli workspace: the existing packages panel (or the customer summary without packages.view). */
export function OrderColliSection() {
  const { order, canViewPackages } = useOrderDetail()
  return (
    <div className="tod-section tod-section-colli" id="sectie-colli">
      {canViewPackages ? (
        <OrderPackagesPanel
          orderId={order.id}
          unloadingStops={order.stops
            .filter((stop) => stop.stopType === 'Unloading')
            .map((stop) => ({ id: stop.id, label: `${stop.sequence}. ${stop.city ?? stop.locationName ?? 'Losstop'}` }))}
        />
      ) : (
        <CustomerPackagesSummary orderId={order.id} />
      )}
    </div>
  )
}
