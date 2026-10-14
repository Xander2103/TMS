import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { getTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE, type TransportOrderDetail } from '../../transport-orders/types'
import { activityPriceText } from '../activityDisplay'
import { ActivityDocumentStatus } from '../documents/ActivityDocumentStatus'
import type { DossierTab } from '../dossierSections'
import { PriceStatusBadge } from '../pricing/PriceStatusBadge'
import type { DossierActivity, DossierDetail } from '../types'
import { DossierGoodsSummary } from './DossierGoodsSummary'
import { DossierRouteSummary } from './DossierRouteSummary'

interface ActivityOrderContextProps {
  dossier: DossierDetail
  /** An order-backed activity (hasStops + linkedTransportOrderId). */
  activity: DossierActivity
  canEdit: boolean
  /** Opens a dossier tab on this activity (route / goods / price / documents) — editing stays in the dossier. */
  onOpenTab: (tab: DossierTab, activity: DossierActivity) => void
}

/**
 * B12: the order of a transport activity, read INSIDE the dossier. Stops, goods, price and
 * documents are the same read components the dossier tabs use (no second order UI); every
 * "Bewerken" lands on the dossier tab that already edits that slice for this activity. The full
 * order page stays one secondary link away (drawer header).
 */
export function ActivityOrderContext({ dossier, activity, canEdit, onOpenTab }: ActivityOrderContextProps) {
  const { t } = useLocale()
  const orderId = activity.linkedTransportOrderId!
  const [loaded, setLoaded] = useState<{ id: string; order: TransportOrderDetail | null } | null>(null)

  useEffect(() => {
    let mounted = true
    getTransportOrder(orderId)
      .then((order) => mounted && setLoaded({ id: orderId, order }))
      .catch(() => mounted && setLoaded({ id: orderId, order: null }))
    return () => {
      mounted = false
    }
  }, [orderId, dossier.version])

  const loading = loaded?.id !== orderId
  const order = loaded?.id === orderId ? loaded.order : null
  const status = order?.status ?? (activity.linkedOrderStatus as TransportOrderDetail['status'] | null)

  return (
    <>
      <section id="ad-sec-overzicht" className="ad-section" aria-labelledby="ad-h-overzicht">
        <h4 id="ad-h-overzicht">{t('dossiers.orderView.overview')}</h4>
        <dl className="ad-facts">
          <div>
            <dt>{t('dossiers.orderView.status')}</dt>
            <dd>{status ? <Badge tone={ORDER_STATUS_TONE[status] ?? 'neutral'}>{t(ORDER_STATUS_LABELS[status])}</Badge> : '—'}</dd>
          </div>
          <div>
            <dt>{t('dossiers.orderView.orderDate')}</dt>
            <dd>{order ? formatDate(order.orderDate) : '—'}</dd>
          </div>
          <div>
            <dt>{t('dossiers.orderView.reference')}</dt>
            <dd>{order?.customerReference || '—'}</dd>
          </div>
          <div>
            <dt>{t('dossiers.orderView.goods')}</dt>
            <dd>{order?.goodsDescription || '—'}</dd>
          </div>
        </dl>
      </section>

      <section id="ad-sec-stops" className="ad-section" aria-labelledby="ad-h-stops">
        <h4 id="ad-h-stops">{t('dossiers.orderView.stops')}</h4>
        <DossierRouteSummary order={order} loading={loading} canEdit={canEdit} onEdit={() => onOpenTab('route', activity)} />
      </section>

      {activity.supportsGoods && (
        <section id="ad-sec-lading" className="ad-section" aria-labelledby="ad-h-lading">
          <h4 id="ad-h-lading">{t('dossiers.orderView.goodsTitle')}</h4>
          <DossierGoodsSummary order={order} loading={loading} canEdit={canEdit} onEdit={() => onOpenTab('goederen', activity)} />
        </section>
      )}

      {activity.isBillable !== false && (
        <section id="ad-sec-prijs" className="ad-section" aria-labelledby="ad-h-prijs">
          <h4 id="ad-h-prijs">{t('dossiers.orderView.price')}</h4>
          <p className="ad-inline">
            <strong>{activityPriceText(t, dossier, activity)}</strong>
            <PriceStatusBadge status={activity.priceStatus} />
            <Button variant="secondary" onClick={() => onOpenTab('prijs', activity)}>
              {activity.priceStatus === 'Priced' || activity.priceStatus === 'Free' ? t('dossiers.orderView.priceOpen') : t('dossiers.orderView.priceDecide')}
            </Button>
          </p>
        </section>
      )}

      <section id="ad-sec-documenten" className="ad-section" aria-labelledby="ad-h-documenten">
        <h4 id="ad-h-documenten">{t('dossiers.orderView.documents')}</h4>
        <ActivityDocumentStatus
          issued={activity.issuedDocuments ?? []}
          documentCount={activity.documentCount ?? 0}
          onOpen={() => onOpenTab('documenten', activity)}
        />
      </section>
    </>
  )
}
