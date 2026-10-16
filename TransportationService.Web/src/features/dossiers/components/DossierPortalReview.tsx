import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { OrderPortalReviewPanel } from '../../transport-orders/components/OrderPortalReviewPanel'
import { submittedPortalOrders } from '../portalReview'
import type { DossierDetail } from '../types'

interface DossierPortalReviewProps {
  dossier: DossierDetail
  /** Reloads the dossier after a review (order status, readiness and owner change server-side). */
  onReviewed: () => void
}

/**
 * M4: the portal review (Accepteren · Info opvragen · Afwijzen) where a dossier-first planner
 * works. It renders the SAME OrderPortalReviewPanel as the order detail — same endpoint, no
 * second copy of the review rules — once per submitted order, and only for users who may change
 * an order's status (the endpoint's own permission).
 */
export function DossierPortalReview({ dossier, onReviewed }: DossierPortalReviewProps) {
  const { t } = useLocale()
  const { hasAnyPermission } = useAuth()
  const pending = submittedPortalOrders(dossier)
  if (pending.length === 0 || !hasAnyPermission(['orders.change_status', 'orders.manage'])) return null

  return (
    <div className="dossier-portal-review">
      {pending.map((order) => (
        <div key={order.orderId}>
          <OrderPortalReviewPanel
            order={{ id: order.orderId, status: 'Submitted' }}
            heading={t('dossiers.portalReview.heading', { number: order.orderNumber })}
            onReviewed={() => onReviewed()}
          />
          <Link className="link-button" to={`/transport-orders/${order.orderId}`}>
            {t('dossiers.portalReview.openOrder', { number: order.orderNumber })}
          </Link>
        </div>
      ))}
    </div>
  )
}
